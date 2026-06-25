from random import choices, randint
from string import ascii_uppercase, digits

from asgiref.sync import async_to_sync
from channels.layers import get_channel_layer
from django.db import IntegrityError, transaction
from django.shortcuts import get_object_or_404
from rest_framework import status
from rest_framework.permissions import IsAuthenticated
from rest_framework.response import Response
from rest_framework.views import APIView

from realtime.message_types import LOBBY_CLOSED, LOBBY_SNAPSHOT
from realtime.room_state import close_room

from .models import Lobby, LobbyDeparture, LobbyMember
from .serializers import (
    CreateLobbySerializer,
    LobbyMemberSerializer,
    LobbySerializer,
    ReadySerializer,
    UpdateLobbySettingsSerializer,
)


CODE_ALPHABET = ascii_uppercase + digits


def generate_lobby_code() -> str:
    while True:
        code = "".join(choices(CODE_ALPHABET, k=6))
        if not Lobby.objects.filter(code=code).exists():
            return code


def next_available_slot(lobby: Lobby) -> int | None:
    used_slots = set(lobby.members.values_list("player_slot", flat=True))
    for slot in range(lobby.max_players):
        if slot not in used_slots:
            return slot
    return None


def generate_map_seed() -> int:
    return randint(1000, 99999999)


def build_lobby_snapshot_payload(lobby: Lobby) -> dict:
    serialized = LobbySerializer(lobby).data
    return {
        "type": LOBBY_SNAPSHOT,
        "lobbyId": serialized["id"],
        "code": serialized["code"],
        "hostId": serialized["hostId"],
        "mapSeed": serialized["mapSeed"],
        "isStarted": serialized["isStarted"],
        "players": serialized["members"],
    }


def broadcast_lobby_event(lobby_id: int, payload: dict) -> None:
    channel_layer = get_channel_layer()
    async_to_sync(channel_layer.group_send)(
        f"lobby_{lobby_id}",
        {
            "type": "broadcast_event",
            "payload": payload,
        },
    )


def broadcast_game_event(lobby_id: int, payload: dict) -> None:
    channel_layer = get_channel_layer()
    async_to_sync(channel_layer.group_send)(
        f"game_{lobby_id}",
        {
            "type": "broadcast_event",
            "payload": payload,
        },
    )


class CreateLobbyView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request):
        serializer = CreateLobbySerializer(data=request.data)
        serializer.is_valid(raise_exception=True)

        with transaction.atomic():
            lobby = Lobby.objects.create(
                code=generate_lobby_code(),
                host=request.user,
                max_players=serializer.validated_data["maxPlayers"],
                map_seed=serializer.validated_data.get("mapSeed", generate_map_seed()),
            )
            LobbyMember.objects.create(lobby=lobby, user=request.user, player_slot=0)

        lobby = Lobby.objects.prefetch_related("members__user").get(pk=lobby.pk)
        return Response(LobbySerializer(lobby).data, status=status.HTTP_201_CREATED)


class JoinLobbyView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request, code: str):
        lobby = get_object_or_404(Lobby.objects.prefetch_related("members__user"), code=code.upper())

        if lobby.is_started:
            return Response({"detail": "Lobby has already started."}, status=status.HTTP_400_BAD_REQUEST)

        if LobbyDeparture.objects.filter(lobby=lobby, user=request.user).exists():
            return Response({"detail": "You already left this lobby and cannot rejoin it."}, status=status.HTTP_403_FORBIDDEN)

        existing_member = lobby.members.filter(user=request.user).first()
        if existing_member is not None:
            return Response(
                {
                    "lobby": LobbySerializer(lobby).data,
                    "member": LobbyMemberSerializer(existing_member).data,
                }
            )

        with transaction.atomic():
            lobby = Lobby.objects.select_for_update().get(pk=lobby.pk)
            slot = next_available_slot(lobby)
            if slot is None:
                return Response({"detail": "Lobby is full."}, status=status.HTTP_400_BAD_REQUEST)

            try:
                member = LobbyMember.objects.create(lobby=lobby, user=request.user, player_slot=slot)
            except IntegrityError:
                return Response({"detail": "Could not join lobby."}, status=status.HTTP_409_CONFLICT)

        lobby = Lobby.objects.prefetch_related("members__user").get(pk=lobby.pk)
        return Response(
            {
                "lobby": LobbySerializer(lobby).data,
                "member": LobbyMemberSerializer(member).data,
            },
            status=status.HTTP_201_CREATED,
        )


class ReadyLobbyView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request, lobby_id: int):
        lobby = get_object_or_404(Lobby, pk=lobby_id)
        member = get_object_or_404(LobbyMember, lobby=lobby, user=request.user)

        serializer = ReadySerializer(data=request.data)
        serializer.is_valid(raise_exception=True)

        member.is_ready = serializer.validated_data["isReady"]
        member.save(update_fields=["is_ready"])

        payload = {
            "type": "player_ready_changed",
            "lobbyId": lobby.id,
            "playerId": member.player_id,
            "userId": member.user_id,
            "slot": member.player_slot,
            "isReady": member.is_ready,
        }
        broadcast_lobby_event(lobby.id, payload)

        return Response(payload)


class UpdateLobbySettingsView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request, lobby_id: int):
        lobby = get_object_or_404(Lobby.objects.prefetch_related("members__user"), pk=lobby_id)
        if lobby.host_id != request.user.id:
            return Response({"detail": "Only the host can change lobby settings."}, status=status.HTTP_403_FORBIDDEN)

        serializer = UpdateLobbySettingsSerializer(data=request.data)
        serializer.is_valid(raise_exception=True)

        next_seed = serializer.validated_data.get("mapSeed")
        if serializer.validated_data.get("randomizeSeed") or next_seed is None:
            next_seed = generate_map_seed()

        lobby.map_seed = next_seed
        lobby.save(update_fields=["map_seed"])

        lobby = Lobby.objects.prefetch_related("members__user").get(pk=lobby.pk)
        payload = build_lobby_snapshot_payload(lobby)
        broadcast_lobby_event(lobby.id, payload)
        return Response(payload)


class StartLobbyView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request, lobby_id: int):
        lobby = get_object_or_404(Lobby.objects.prefetch_related("members__user"), pk=lobby_id)

        if lobby.host_id != request.user.id:
            return Response({"detail": "Only the host can start this lobby."}, status=status.HTTP_403_FORBIDDEN)

        members = list(lobby.members.all())
        if not members:
            return Response({"detail": "Cannot start an empty lobby."}, status=status.HTTP_400_BAD_REQUEST)

        if not all(member.is_ready for member in members):
            return Response({"detail": "All lobby members must be ready before start."}, status=status.HTTP_400_BAD_REQUEST)

        lobby.is_started = True
        lobby.save(update_fields=["is_started"])

        payload = {
            "type": "game_started",
            "lobbyId": lobby.id,
            "mapId": request.data.get("mapId", f"seed_{lobby.map_seed}"),
            "mapSeed": lobby.map_seed,
            "players": [
                {
                    "playerId": member.player_id,
                    "userId": member.user_id,
                    "slot": member.player_slot,
                }
                for member in members
            ],
        }
        broadcast_lobby_event(lobby.id, payload)

        return Response(payload)


class LeaveLobbyView(APIView):
    permission_classes = [IsAuthenticated]

    def post(self, request, lobby_id: int):
        lobby = get_object_or_404(Lobby.objects.prefetch_related("members__user"), pk=lobby_id)
        member = get_object_or_404(LobbyMember, lobby=lobby, user=request.user)
        is_host = lobby.host_id == request.user.id

        if is_host:
            payload = {
                "type": LOBBY_CLOSED,
                "lobbyId": lobby.id,
                "reason": "host_left",
                "message": "The host left, so the lobby was closed.",
            }
            broadcast_lobby_event(lobby.id, payload)
            broadcast_game_event(lobby.id, payload)
            close_room(lobby.id)
            lobby.delete()
            return Response({"detail": "Host left. Lobby closed."}, status=status.HTTP_200_OK)

        with transaction.atomic():
            LobbyDeparture.objects.get_or_create(lobby=lobby, user=request.user)
            member.delete()

        return Response({"detail": "You left the lobby."}, status=status.HTTP_200_OK)


class LobbyDetailView(APIView):
    permission_classes = [IsAuthenticated]

    def get(self, request, lobby_id: int):
        lobby = get_object_or_404(Lobby.objects.prefetch_related("members__user"), pk=lobby_id)

        if not lobby.members.filter(user=request.user).exists():
            return Response({"detail": "You are not a member of this lobby."}, status=status.HTTP_403_FORBIDDEN)

        return Response(LobbySerializer(lobby).data)

from django.contrib.auth import get_user_model
from django.test import TestCase, override_settings
from rest_framework.authtoken.models import Token
from rest_framework.test import APIClient

from .models import LobbyDeparture


@override_settings(CHANNEL_LAYERS={"default": {"BACKEND": "channels.layers.InMemoryChannelLayer"}})
class LobbyApiTests(TestCase):
    def setUp(self):
        self.user = get_user_model().objects.create_user(username="host")
        self.token = Token.objects.create(user=self.user)
        self.client = APIClient()
        self.client.credentials(HTTP_AUTHORIZATION=f"Token {self.token.key}")

    def test_create_lobby_adds_host_as_slot_zero(self):
        response = self.client.post("/api/lobbies/create/", {"maxPlayers": 4}, format="json")

        self.assertEqual(response.status_code, 201)
        self.assertEqual(response.data["maxPlayers"], 4)
        self.assertIn("mapSeed", response.data)
        self.assertEqual(response.data["members"][0]["slot"], 0)
        self.assertEqual(response.data["members"][0]["playerId"], f"player_{self.user.id}")

    def test_join_lobby_assigns_next_slot(self):
        lobby_response = self.client.post("/api/lobbies/create/", {"maxPlayers": 4}, format="json")
        code = lobby_response.data["code"]

        guest = get_user_model().objects.create_user(username="guest")
        guest_token = Token.objects.create(user=guest)
        guest_client = APIClient()
        guest_client.credentials(HTTP_AUTHORIZATION=f"Token {guest_token.key}")

        response = guest_client.post(f"/api/lobbies/{code}/join/", {}, format="json")

        self.assertEqual(response.status_code, 201)
        self.assertEqual(response.data["member"]["slot"], 1)

    def test_host_can_update_lobby_seed(self):
        lobby_response = self.client.post("/api/lobbies/create/", {"maxPlayers": 4, "mapSeed": 1111}, format="json")
        lobby_id = lobby_response.data["id"]

        response = self.client.post(f"/api/lobbies/{lobby_id}/settings/", {"mapSeed": 2222}, format="json")

        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.data["mapSeed"], 2222)

    def test_non_host_leave_blocks_rejoin(self):
        lobby_response = self.client.post("/api/lobbies/create/", {"maxPlayers": 4}, format="json")
        code = lobby_response.data["code"]
        lobby_id = lobby_response.data["id"]

        guest = get_user_model().objects.create_user(username="guest")
        guest_token = Token.objects.create(user=guest)
        guest_client = APIClient()
        guest_client.credentials(HTTP_AUTHORIZATION=f"Token {guest_token.key}")

        join_response = guest_client.post(f"/api/lobbies/{code}/join/", {}, format="json")
        self.assertEqual(join_response.status_code, 201)

        leave_response = guest_client.post(f"/api/lobbies/{lobby_id}/leave/", {}, format="json")
        self.assertEqual(leave_response.status_code, 200)
        self.assertTrue(LobbyDeparture.objects.filter(lobby_id=lobby_id, user=guest).exists())

        rejoin_response = guest_client.post(f"/api/lobbies/{code}/join/", {}, format="json")
        self.assertEqual(rejoin_response.status_code, 403)

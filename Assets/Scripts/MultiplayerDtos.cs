using System;
using UnityEngine;

[Serializable]
public sealed class UserDto
{
    public int id;
    public string username;
}

[Serializable]
public sealed class TokenResponseDto
{
    public string token;
    public UserDto user;
}

[Serializable]
public sealed class LobbyMemberDto
{
    public int userId;
    public string username;
    public string playerId;
    public int slot;
    public bool isReady;
    public string joined_at;
}

[Serializable]
public sealed class LobbyDto
{
    public int id;
    public string code;
    public int hostId;
    public int maxPlayers;
    public bool isStarted;
    public string created_at;
    public LobbyMemberDto[] members;
}

[Serializable]
public sealed class JoinLobbyResponseDto
{
    public LobbyDto lobby;
    public LobbyMemberDto member;
}

[Serializable]
public sealed class CreateLobbyRequestDto
{
    public int maxPlayers = 4;
}

[Serializable]
public sealed class ReadyRequestDto
{
    public bool isReady;
}

[Serializable]
public sealed class StartLobbyRequestDto
{
    public string mapId = "test_map";
}

[Serializable]
public sealed class SocketTypeEnvelopeDto
{
    public string type;
}

[Serializable]
public sealed class PingDto
{
    public string type = "ping";
    public double clientTime;
}

[Serializable]
public sealed class HeartbeatDto
{
    public string type = "heartbeat";
    public double clientTime;
}

[Serializable]
public sealed class PongDto
{
    public string type;
    public double clientTime;
    public double serverTime;
}

[Serializable]
public sealed class LobbySnapshotDto
{
    public string type;
    public int lobbyId;
    public string code;
    public int hostId;
    public bool isStarted;
    public LobbyMemberDto[] players;
}

[Serializable]
public sealed class LobbyEventDto
{
    public string type;
    public int lobbyId;
    public string playerId;
    public int userId;
    public int slot;
    public bool isReady;
}

[Serializable]
public sealed class GameStartedDto
{
    public string type;
    public int lobbyId;
    public string mapId;
    public GameStartedPlayerDto[] players;
}

[Serializable]
public sealed class GameStartedPlayerDto
{
    public string playerId;
    public int userId;
    public int slot;
}

[Serializable]
public sealed class PlayerStateDto
{
    public string type = "player_state";
    public string playerId;
    public int userId;
    public int seq;
    public double clientTime;
    public double serverTime;
    public float[] position;
    public float[] rotation;
    public float[] velocity;
    public float[] moveTarget;
    public float[] aimTarget;
    public float[] gaitForward;
    public float[] leftArmTarget;
    public float[] rightArmTarget;
    public string heldObjectType = "none";
    public string heldItemType = "";
    public string animationState = "idle";
    public string actionState = "idle";
    public int actionSeq;

    public static PlayerStateDto FromTransform(string playerId, int userId, int seq, Transform transform, Vector3 velocity)
    {
        return new PlayerStateDto
        {
            type = "player_state",
            playerId = playerId,
            userId = userId,
            seq = seq,
            clientTime = Time.realtimeSinceStartupAsDouble,
            position = MultiplayerJson.VectorToArray(transform.position),
            rotation = MultiplayerJson.VectorToArray(transform.eulerAngles),
            velocity = MultiplayerJson.VectorToArray(velocity),
            animationState = velocity.sqrMagnitude > 0.01f ? "run" : "idle",
        };
    }

    public static PlayerStateDto FromProceduralPlayer(
        string playerId,
        int userId,
        int seq,
        ProceduralPlayerRig rig)
    {
        Transform core = rig != null ? rig.CoreNode : null;
        Vector3 position = core != null ? core.position : Vector3.zero;
        Vector3 rotation = core != null ? core.eulerAngles : Vector3.zero;
        Vector3 velocity = rig != null ? rig.Velocity : Vector3.zero;
        Transform runTarget = rig != null ? rig.RunTarget : null;
        Transform aimTarget = rig != null ? rig.AimTarget : null;
        PlayerWeaponPickup weaponPickup = rig != null ? rig.GetComponent<PlayerWeaponPickup>() : null;
        PlayerItemPickup itemPickup = rig != null ? rig.GetComponent<PlayerItemPickup>() : null;

        string heldObjectType = "none";
        string heldItemType = "";

        if (weaponPickup != null && weaponPickup.HasWeapon)
        {
            heldObjectType = "weapon";
            heldItemType = "spear";
        }
        else if (itemPickup != null && itemPickup.HasItem)
        {
            heldObjectType = "item";
            heldItemType = itemPickup.HeldItem != null
                ? itemPickup.HeldItem.ItemType.ToString()
                : "";
        }

        return new PlayerStateDto
        {
            type = "player_state",
            playerId = playerId,
            userId = userId,
            seq = seq,
            clientTime = Time.realtimeSinceStartupAsDouble,
            position = MultiplayerJson.VectorToArray(position),
            rotation = MultiplayerJson.VectorToArray(rotation),
            velocity = MultiplayerJson.VectorToArray(velocity),
            moveTarget = MultiplayerJson.VectorToArray(runTarget != null ? runTarget.position : position),
            aimTarget = MultiplayerJson.VectorToArray(aimTarget != null ? aimTarget.position : position + Vector3.forward),
            gaitForward = MultiplayerJson.VectorToArray(rig != null ? rig.GaitForward : Vector3.forward),
            leftArmTarget = MultiplayerJson.VectorToArray(rig != null ? rig.LeftArmTargetWorld : position),
            rightArmTarget = MultiplayerJson.VectorToArray(rig != null ? rig.RightArmTargetWorld : position),
            heldObjectType = heldObjectType,
            heldItemType = heldItemType,
            animationState = velocity.sqrMagnitude > 0.01f ? "run" : "idle",
            actionState = rig != null ? rig.ActionState : "idle",
            actionSeq = rig != null ? rig.ActionSequence : 0,
        };
    }
}

[Serializable]
public sealed class RoomSnapshotDto
{
    public string type;
    public int lobbyId;
    public PlayerStateDto[] players;
    public MammothStateDto mammothState;
    public MammothHealthDto mammothHealth;
}

[Serializable]
public sealed class MammothHealthDto
{
    public string type = "mammoth_health";
    public int lobbyId;
    public string enemyId = "mammoth";
    public int currentHealth;
    public int maxHealth;
    public int damage;
    public double serverTime;

    public static MammothHealthDto FromEnemyHealth(int lobbyId, EnemyHealth enemyHealth, int damage)
    {
        return new MammothHealthDto
        {
            type = "mammoth_health",
            lobbyId = lobbyId,
            enemyId = "mammoth",
            currentHealth = enemyHealth != null ? enemyHealth.CurrentHealth : 0,
            maxHealth = enemyHealth != null ? enemyHealth.MaxHealth : 0,
            damage = Mathf.Max(0, damage),
        };
    }
}

[Serializable]
public sealed class MammothStateDto
{
    public string type = "mammoth_state";
    public int lobbyId;
    public string enemyId = "mammoth";
    public int authoritativeUserId;
    public int currentHealth;
    public int maxHealth;
    public int damage;
    public double serverTime;
    public float[] position;
    public float[] rotation;

    public static MammothStateDto FromEnemyHealth(int lobbyId, int authoritativeUserId, EnemyHealth enemyHealth)
    {
        Transform enemyTransform = enemyHealth != null ? enemyHealth.transform : null;
        Vector3 position = enemyTransform != null ? enemyTransform.position : Vector3.zero;
        Vector3 rotation = enemyTransform != null ? enemyTransform.eulerAngles : Vector3.zero;

        return new MammothStateDto
        {
            type = "mammoth_state",
            lobbyId = lobbyId,
            enemyId = "mammoth",
            authoritativeUserId = authoritativeUserId,
            currentHealth = enemyHealth != null ? enemyHealth.CurrentHealth : 0,
            maxHealth = enemyHealth != null ? enemyHealth.MaxHealth : 0,
            damage = 0,
            position = MultiplayerJson.VectorToArray(position),
            rotation = MultiplayerJson.VectorToArray(rotation),
        };
    }
}

[Serializable]
public sealed class ErrorDetailDto
{
    public string detail;
}

public struct ApiResult<T>
{
    public readonly bool IsSuccess;
    public readonly T Value;
    public readonly string Error;

    private ApiResult(bool isSuccess, T value, string error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static ApiResult<T> Success(T value)
    {
        return new ApiResult<T>(true, value, null);
    }

    public static ApiResult<T> Failure(string error)
    {
        return new ApiResult<T>(false, default(T), error);
    }
}

public static class MultiplayerJson
{
    public static float[] VectorToArray(Vector3 value)
    {
        return new[] { value.x, value.y, value.z };
    }

    public static Vector3 ArrayToVector(float[] value)
    {
        if (value == null || value.Length < 3)
        {
            return Vector3.zero;
        }

        return new Vector3(value[0], value[1], value[2]);
    }

    public static string ExtractError(string json, string fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            ErrorDetailDto error = JsonUtility.FromJson<ErrorDetailDto>(json);
            if (!string.IsNullOrWhiteSpace(error.detail))
            {
                return error.detail;
            }
        }
        catch (ArgumentException)
        {
        }

        return $"{fallback}: {json}";
    }
}

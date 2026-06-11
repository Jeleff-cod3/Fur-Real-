using System.Collections;
using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
public class PrototypePlayerRespawn : MonoBehaviour
{
    [SerializeField] private float respawnDelaySeconds = 2.5f;
    [SerializeField] private float respawnInvulnerabilitySeconds = 1.25f;
    [SerializeField] private bool dropWeaponOnDeath = true;

    private PlayerHealth playerHealth;
    private Rigidbody body;
    private LocalCubeController localController;
    private PlayerCombat playerCombat;
    private PlayerWeaponPickup weaponPickup;
    private PlayerMouseAim mouseAim;
    private Coroutine respawnRoutine;

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        body = GetComponent<Rigidbody>();
        localController = GetComponent<LocalCubeController>();
        playerCombat = GetComponent<PlayerCombat>();
        weaponPickup = GetComponent<PlayerWeaponPickup>();
        mouseAim = GetComponent<PlayerMouseAim>();
    }

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died += HandlePlayerDied;
        }
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died -= HandlePlayerDied;
        }
    }

    private void HandlePlayerDied(PlayerHealth deadPlayer)
    {
        if (respawnRoutine != null)
        {
            StopCoroutine(respawnRoutine);
        }

        respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        if (dropWeaponOnDeath && weaponPickup != null)
        {
            weaponPickup.DropEquippedWeaponIfAny();
        }

        SetPlayerControlEnabled(false);

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (respawnDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(respawnDelaySeconds);
        }

        Vector3 respawnPosition = ResolveRespawnPosition();
        transform.SetPositionAndRotation(respawnPosition, Quaternion.identity);

        if (body != null)
        {
            body.position = respawnPosition;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        playerHealth.RestoreFullHealth();
        playerHealth.SetInvulnerableFor(respawnInvulnerabilitySeconds);

        SetPlayerControlEnabled(true);
        respawnRoutine = null;
    }

    private Vector3 ResolveRespawnPosition()
    {
        if (MultiplayerPrototype.TryGetLocalRespawnPosition(out Vector3 multiplayerRespawn))
        {
            return multiplayerRespawn;
        }

        WorldChunkRenderer worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        if (worldChunkRenderer != null)
        {
            return worldChunkRenderer.GetArenaCenterWorldPosition(1f);
        }

        return transform.position + Vector3.up;
    }

    private void SetPlayerControlEnabled(bool isEnabled)
    {
        if (localController != null)
        {
            localController.enabled = isEnabled;
        }

        if (playerCombat != null)
        {
            playerCombat.enabled = isEnabled;
        }

        if (weaponPickup != null)
        {
            weaponPickup.enabled = isEnabled;
        }

        if (mouseAim != null)
        {
            mouseAim.enabled = isEnabled;
        }
    }
}

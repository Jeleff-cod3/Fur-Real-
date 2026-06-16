using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCrafting : MonoBehaviour
{
    [Header("Crafting")]
    [SerializeField] private float craftSearchRadius = 2.4f;
    [SerializeField] private float ingredientPairMaxDistance = 1.4f;
    [SerializeField] private bool autoEquipCraftedSpear = true;

    [Header("Spawn")]
    [SerializeField] private float groundProbeHeight = 40f;
    [SerializeField] private float craftedSpearHeight = 0.75f;
    [SerializeField] private LayerMask craftGroundLayers = ~0;
    [SerializeField] private PickupableWeapon craftedSpearPrefab;

    private readonly Collider[] overlapBuffer = new Collider[64];
    private readonly List<PickupableItem> nearbyCraftItems = new List<PickupableItem>();

    private PlayerWeaponPickup weaponPickup;
    private WorldChunkRenderer worldChunkRenderer;

    private void Awake()
    {
        weaponPickup = GetComponent<PlayerWeaponPickup>();
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.cKey.wasPressedThisFrame)
        {
            return;
        }

        TryCraftNearbyRecipe();
    }

    private void TryCraftNearbyRecipe()
    {
        if (weaponPickup == null)
        {
            weaponPickup = GetComponent<PlayerWeaponPickup>();
        }

        CollectNearbyDroppedItems();

        PickupableItem stick = null;
        PickupableItem rock = null;
        float closestPairDistance = float.MaxValue;

        for (int i = 0; i < nearbyCraftItems.Count; i++)
        {
            PickupableItem first = nearbyCraftItems[i];
            for (int j = i + 1; j < nearbyCraftItems.Count; j++)
            {
                PickupableItem second = nearbyCraftItems[j];

                if (!IsStickAndRock(first, second))
                {
                    continue;
                }

                float pairDistance = Vector3.Distance(first.transform.position, second.transform.position);
                if (pairDistance > ingredientPairMaxDistance || pairDistance >= closestPairDistance)
                {
                    continue;
                }

                closestPairDistance = pairDistance;
                stick = first.ItemType == PickupItemType.Stick ? first : second;
                rock = first.ItemType == PickupItemType.Rock ? first : second;
            }
        }

        if (stick == null || rock == null)
        {
            Debug.Log("No valid dropped stick + rock pair found for crafting.");
            return;
        }

        PickupableWeapon spearPrefab = ResolveCraftedSpearPrefab();
        if (spearPrefab == null)
        {
            Debug.LogWarning("Crafting failed because no spear prefab could be resolved.");
            return;
        }

        Vector3 spawnPosition = ResolveCraftedSpearPosition(stick.transform.position, rock.transform.position);
        PickupableWeapon craftedSpear = Instantiate(
            spearPrefab,
            spawnPosition,
            Quaternion.Euler(0f, 90f, 0f)
        );
        craftedSpear.name = "Crafted Spear";

        stick.Consume();
        rock.Consume();

        if (autoEquipCraftedSpear && weaponPickup != null)
        {
            weaponPickup.TryPickUpSpecificWeapon(craftedSpear);
        }
    }

    private void CollectNearbyDroppedItems()
    {
        nearbyCraftItems.Clear();

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            craftSearchRadius,
            overlapBuffer,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide
        );

        HashSet<PickupableItem> uniqueItems = new HashSet<PickupableItem>();

        for (int i = 0; i < hitCount; i++)
        {
            PickupableItem item = overlapBuffer[i] != null
                ? overlapBuffer[i].GetComponent<PickupableItem>()
                : null;

            if (item == null || !item.CanBeUsedForCrafting || !uniqueItems.Add(item))
            {
                continue;
            }

            nearbyCraftItems.Add(item);
        }
    }

    private PickupableWeapon ResolveCraftedSpearPrefab()
    {
        if (craftedSpearPrefab != null)
        {
            return craftedSpearPrefab;
        }

        if (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        }

        if (worldChunkRenderer != null && worldChunkRenderer.CraftedSpearPrefab != null)
        {
            return worldChunkRenderer.CraftedSpearPrefab;
        }

        SpearTestSpawner spearSpawner = FindAnyObjectByType<SpearTestSpawner>();
        return spearSpawner != null ? spearSpawner.SpearPrefab : null;
    }

    private Vector3 ResolveCraftedSpearPosition(Vector3 firstPosition, Vector3 secondPosition)
    {
        Vector3 midpoint = (firstPosition + secondPosition) * 0.5f;
        Vector3 rayOrigin = midpoint + Vector3.up * groundProbeHeight;

        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                groundProbeHeight * 2f,
                craftGroundLayers,
                QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * craftedSpearHeight;
        }

        return midpoint + Vector3.up * craftedSpearHeight;
    }

    private static bool IsStickAndRock(PickupableItem first, PickupableItem second)
    {
        return (first.ItemType == PickupItemType.Stick && second.ItemType == PickupItemType.Rock) ||
               (first.ItemType == PickupItemType.Rock && second.ItemType == PickupItemType.Stick);
    }
}

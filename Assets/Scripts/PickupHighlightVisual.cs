using System.Collections.Generic;
using UnityEngine;

public class PickupHighlightVisual : MonoBehaviour
{
    [SerializeField] private float activationRadius = 2.6f;
    [SerializeField] private float scaleMultiplier = 1.08f;
    [SerializeField] private float playerLookupInterval = 0.4f;

    private readonly List<MeshRenderer> highlightRenderers = new List<MeshRenderer>();
    private static Material outlineMaterial;

    private PickupableWeapon pickupableWeapon;
    private PickupableItem pickupableItem;
    private Transform playerTransform;
    private float nextPlayerLookupTime;

    public static PickupHighlightVisual EnsureAttached(GameObject target)
    {
        if (target == null)
        {
            return null;
        }

        PickupHighlightVisual highlight = target.GetComponent<PickupHighlightVisual>();
        if (highlight == null)
        {
            highlight = target.AddComponent<PickupHighlightVisual>();
        }

        highlight.RebuildVisualsIfNeeded();
        return highlight;
    }

    private void Awake()
    {
        pickupableWeapon = GetComponent<PickupableWeapon>();
        pickupableItem = GetComponent<PickupableItem>();
    }

    private void Start()
    {
        RebuildVisualsIfNeeded();
        SetHighlightEnabled(false);
    }

    private void Update()
    {
        if (Time.time >= nextPlayerLookupTime)
        {
            nextPlayerLookupTime = Time.time + playerLookupInterval;
            ResolvePlayerTransform();
        }

        bool shouldShow = ShouldShowHighlight();
        SetHighlightEnabled(shouldShow);
    }

    public void RebuildVisualsIfNeeded()
    {
        if (highlightRenderers.Count > 0)
        {
            return;
        }

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
        Material outlineMaterial = GetOutlineMaterial();

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            MeshRenderer sourceRenderer = meshFilter.GetComponent<MeshRenderer>();
            if (sourceRenderer == null)
            {
                continue;
            }

            if (meshFilter.transform.GetComponent<PickupHighlightGhost>() != null)
            {
                continue;
            }

            GameObject ghostObject = new GameObject("PickupHighlight");
            ghostObject.transform.SetParent(meshFilter.transform, false);
            ghostObject.transform.localPosition = Vector3.zero;
            ghostObject.transform.localRotation = Quaternion.identity;
            ghostObject.transform.localScale = Vector3.one * scaleMultiplier;
            ghostObject.layer = meshFilter.gameObject.layer;

            ghostObject.AddComponent<PickupHighlightGhost>();

            MeshFilter ghostFilter = ghostObject.AddComponent<MeshFilter>();
            ghostFilter.sharedMesh = meshFilter.sharedMesh;

            MeshRenderer ghostRenderer = ghostObject.AddComponent<MeshRenderer>();
            ghostRenderer.sharedMaterial = outlineMaterial;
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghostRenderer.receiveShadows = false;
            ghostRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            ghostRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            ghostRenderer.enabled = false;

            highlightRenderers.Add(ghostRenderer);
        }
    }

    private bool ShouldShowHighlight()
    {
        if (!CanObjectBePickedUp())
        {
            return false;
        }

        if (playerTransform == null)
        {
            return false;
        }

        return (playerTransform.position - transform.position).sqrMagnitude <= activationRadius * activationRadius;
    }

    private bool CanObjectBePickedUp()
    {
        if (pickupableItem != null)
        {
            return pickupableItem.CanBePickedUpFromWorld;
        }

        if (pickupableWeapon != null)
        {
            return pickupableWeapon.CanBePickedUpFromWorld;
        }

        return false;
    }

    private void ResolvePlayerTransform()
    {
        if (playerTransform != null && playerTransform.gameObject.scene.IsValid())
        {
            return;
        }

        PlayerCarryController carryController = Object.FindAnyObjectByType<PlayerCarryController>();
        if (carryController != null)
        {
            playerTransform = carryController.transform;
            return;
        }

        PlayerWeaponPickup weaponPickup = Object.FindAnyObjectByType<PlayerWeaponPickup>();
        if (weaponPickup != null)
        {
            playerTransform = weaponPickup.transform;
        }
    }

    private void SetHighlightEnabled(bool enabled)
    {
        for (int i = 0; i < highlightRenderers.Count; i++)
        {
            if (highlightRenderers[i] != null)
            {
                highlightRenderers[i].enabled = enabled;
            }
        }
    }

    private static Material GetOutlineMaterial()
    {
        if (outlineMaterial != null)
        {
            return outlineMaterial;
        }

        Shader shader = Shader.Find("Custom/PickupOutline");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        outlineMaterial = new Material(shader);
        outlineMaterial.name = "PickupOutlineRuntime";

        if (outlineMaterial.HasProperty("_BaseColor"))
        {
            outlineMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.92f));
        }

        if (outlineMaterial.HasProperty("_Color"))
        {
            outlineMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 0.92f));
        }

        return outlineMaterial;
    }

    private sealed class PickupHighlightGhost : MonoBehaviour
    {
    }
}

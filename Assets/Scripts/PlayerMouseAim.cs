using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMouseAim : MonoBehaviour
{
    [Header("Shared Mouse Ground Position")]
    [SerializeField] private MouseWorldPosition mouseWorldPosition;
    [SerializeField] private bool autoCreateMouseWorldPosition = true;

    [Header("Aim Targeting")]
    [SerializeField] private LayerMask aimSurfaceLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private float aimRayDistance = 1000f;
    [SerializeField] private bool preferGroundPlaneForTopDown = true;
    [SerializeField] private bool useGroundPlaneFallback = true;

    private void Awake()
    {
        EnsureMouseWorldPositionService();
    }

    public bool IsAimModifierPressed
    {
        get
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }
    }

    public bool TryGetAimDirection(out Vector3 aimDirection, bool requireFrontHemisphere)
    {
        aimDirection = Vector3.zero;

        if (!TryGetMouseWorldPoint(out Vector3 targetPoint))
        {
            return false;
        }

        Vector3 flatDirection = targetPoint - transform.position;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude < 0.001f)
        {
            return false;
        }

        aimDirection = flatDirection.normalized;

        if (requireFrontHemisphere && Vector3.Dot(transform.forward, aimDirection) < 0f)
        {
            aimDirection = Vector3.zero;
            return false;
        }

        return true;
    }

    public bool TryGetMouseWorldPoint(out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;

        EnsureMouseWorldPositionService();

        if (mouseWorldPosition != null)
        {
            mouseWorldPosition.Refresh();

            if (!mouseWorldPosition.HasHitGround)
            {
                return false;
            }

            worldPoint = mouseWorldPosition.WorldPosition;
            return true;
        }

        Camera aimCamera = Camera.main;
        Mouse mouse = Mouse.current;

        if (aimCamera == null || mouse == null)
        {
            return false;
        }

        Ray aimRay = aimCamera.ScreenPointToRay(mouse.position.ReadValue());

        if (preferGroundPlaneForTopDown && TryGetGroundPlanePoint(aimRay, out worldPoint))
        {
            return true;
        }

        int surfaceMask = aimSurfaceLayers.value == 0
            ? Physics.DefaultRaycastLayers
            : aimSurfaceLayers.value;

        RaycastHit[] hits = Physics.RaycastAll(
            aimRay,
            aimRayDistance,
            surfaceMask,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance >= closestDistance)
            {
                continue;
            }

            closestDistance = hit.distance;
            worldPoint = hit.point;
        }

        if (closestDistance < float.MaxValue)
        {
            return true;
        }

        if (!useGroundPlaneFallback)
        {
            return false;
        }

        return TryGetGroundPlanePoint(aimRay, out worldPoint);
    }

    private void EnsureMouseWorldPositionService()
    {
        if (mouseWorldPosition != null)
        {
            return;
        }

        mouseWorldPosition = MouseWorldPosition.Instance;

        if (mouseWorldPosition != null)
        {
            return;
        }

        mouseWorldPosition = FindAnyObjectByType<MouseWorldPosition>();

        if (mouseWorldPosition != null || !autoCreateMouseWorldPosition)
        {
            return;
        }

        Camera aimCamera = ResolveAimCameraForService();

        if (aimCamera == null)
        {
            return;
        }

        mouseWorldPosition = aimCamera.GetComponent<MouseWorldPosition>();

        if (mouseWorldPosition == null)
        {
            mouseWorldPosition = aimCamera.gameObject.AddComponent<MouseWorldPosition>();
        }
    }

    private Camera ResolveAimCameraForService()
    {
        if (Camera.main != null)
        {
            return Camera.main;
        }

        Camera[] cameras = FindObjectsByType<Camera>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (Camera candidate in cameras)
        {
            if (candidate != null && candidate.enabled && candidate.targetTexture != null)
            {
                return candidate;
            }
        }

        foreach (Camera candidate in cameras)
        {
            if (candidate != null && candidate.enabled)
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryGetGroundPlanePoint(Ray aimRay, out Vector3 worldPoint)
    {
        Plane fallbackPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));

        if (fallbackPlane.Raycast(aimRay, out float enterDistance))
        {
            worldPoint = aimRay.GetPoint(enterDistance);
            return true;
        }

        worldPoint = Vector3.zero;
        return false;
    }
}

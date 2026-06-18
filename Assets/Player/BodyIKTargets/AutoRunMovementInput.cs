using UnityEngine;

public class AutoRunMovementInput : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("The body/core/player transform used as the center for WASD target positions.")]
    public Transform player;

    [Tooltip("The run target consumed by AutoRunLegPairController.")]
    public Transform target;

    [Tooltip("Object that always follows the mouse cursor world position.")]
    public Transform mouseTracker;

    [Tooltip("Optional camera transform. If empty, Camera.main is used.")]
    public Transform cameraTransform;

    [Header("Target Placement")]
    [Min(0f)]
    public float targetDistance = 3f;

    [Tooltip("Keeps cursor targets on the player's current height.")]
    public bool usePlayerHeightForMousePlane = true;

    [Tooltip("Used when usePlayerHeightForMousePlane is false.")]
    public float mousePlaneHeight = 0f;

    private const float Epsilon = 0.0001f;

    private void Update()
    {
        Vector3 mouseWorldPosition;
        bool hasMouseWorldPosition = TryGetMouseWorldPosition(out mouseWorldPosition);

        if (hasMouseWorldPosition && mouseTracker != null)
        {
            mouseTracker.position = mouseWorldPosition;
        }

        if (target == null)
        {
            return;
        }

        if (IsShiftHeld())
        {
            if (hasMouseWorldPosition)
            {
                target.position = mouseWorldPosition;
            }

            return;
        }

        if (player == null)
        {
            return;
        }

        Vector3 inputDirection = GetWasdDirection();
        if (inputDirection.sqrMagnitude <= Epsilon)
        {
            return;
        }

        target.position = player.position + inputDirection.normalized * targetDistance;
    }

    private Vector3 GetWasdDirection()
    {
        Vector3 direction = Vector3.zero;

        if (Input.GetKey(KeyCode.W))
        {
            direction += Vector3.forward;
        }

        if (Input.GetKey(KeyCode.S))
        {
            direction += Vector3.back;
        }

        if (Input.GetKey(KeyCode.D))
        {
            direction += Vector3.right;
        }

        if (Input.GetKey(KeyCode.A))
        {
            direction += Vector3.left;
        }

        return direction;
    }

    private bool TryGetMouseWorldPosition(out Vector3 worldPosition)
    {
        Camera inputCamera = GetInputCamera();
        if (inputCamera == null)
        {
            worldPosition = Vector3.zero;
            return false;
        }

        float planeHeight = usePlayerHeightForMousePlane && player != null
            ? player.position.y
            : mousePlaneHeight;

        Plane mousePlane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
        Ray mouseRay = inputCamera.ScreenPointToRay(Input.mousePosition);

        if (!mousePlane.Raycast(mouseRay, out float enter))
        {
            worldPosition = Vector3.zero;
            return false;
        }

        worldPosition = mouseRay.GetPoint(enter);
        return true;
    }

    private Camera GetInputCamera()
    {
        if (cameraTransform != null && cameraTransform.TryGetComponent(out Camera camera))
        {
            return camera;
        }

        return Camera.main;
    }

    private static bool IsShiftHeld()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }
}

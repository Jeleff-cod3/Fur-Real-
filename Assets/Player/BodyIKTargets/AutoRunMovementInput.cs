using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-125)]
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

    [Tooltip("Optional leg controller to receive the held gait direction.")]
    public AutoRunLegPairController legController;

    [Header("Target Placement")]
    [Min(0f)]
    public float targetDistance = 3f;

    [Tooltip("Keeps cursor targets on the player's current height.")]
    public bool usePlayerHeightForMousePlane = true;

    [Tooltip("Used when usePlayerHeightForMousePlane is false.")]
    public float mousePlaneHeight = 0f;

    [Header("Gait-Relative WASD")]
    [Tooltip("Shift aims/rotates the leg assembly. WASD then moves relative to that held direction instead of world +Z/+X.")]
    public bool moveRelativeToHeldGaitForward = true;

    [Tooltip("When true, Shift does not make the run target chase the mouse. It only updates the held gait forward.")]
    public bool shiftAimsGaitOnly = true;

    [Tooltip("If true, standing still continuously pins the run target to the player/core so momentum does not keep chasing an old target.")]
    public bool stopTargetWhenNoInput = true;

    [Min(0f)]
    public float gaitTurnSpeedDegrees = 900f;

    private const float Epsilon = 0.0001f;
    private Vector3 heldGaitForward = Vector3.forward;
    private bool hasHeldGaitForward;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();

        Vector3 mouseWorldPosition;
        bool hasMouseWorldPosition = TryGetMouseWorldPosition(out mouseWorldPosition);

        if (hasMouseWorldPosition && mouseTracker != null)
        {
            mouseTracker.position = mouseWorldPosition;
        }

        if (target == null || player == null)
        {
            return;
        }

        EnsureHeldGaitForward();

        bool shiftHeld = IsShiftHeld();
        if (shiftHeld && hasMouseWorldPosition)
        {
            Vector3 toMouse = Vector3.ProjectOnPlane(mouseWorldPosition - player.position, Vector3.up);
            if (toMouse.sqrMagnitude > Epsilon)
            {
                Vector3 desiredForward = toMouse.normalized;
                heldGaitForward = gaitTurnSpeedDegrees > 0f
                    ? Vector3.RotateTowards(
                        heldGaitForward,
                        desiredForward,
                        gaitTurnSpeedDegrees * Mathf.Deg2Rad * Time.deltaTime,
                        0f).normalized
                    : desiredForward;
            }
        }

        PushGaitForwardToController();

        Vector2 inputAxes = GetWasdAxes();
        if (inputAxes.sqrMagnitude <= Epsilon)
        {
            if (stopTargetWhenNoInput)
            {
                target.position = player.position;
            }
            else if (shiftHeld && hasMouseWorldPosition && !shiftAimsGaitOnly)
            {
                target.position = mouseWorldPosition;
            }

            return;
        }

        Vector3 moveDirection = moveRelativeToHeldGaitForward
            ? GetMoveDirectionInHeldGaitBasis(inputAxes)
            : new Vector3(inputAxes.x, 0f, inputAxes.y);

        if (moveDirection.sqrMagnitude <= Epsilon)
        {
            return;
        }

        target.position = player.position + moveDirection.normalized * targetDistance;
    }

    private void ResolveReferences()
    {
        if (legController == null)
        {
            legController = GetComponentInChildren<AutoRunLegPairController>(true);
        }

        if (player == null && legController != null)
        {
            player = legController.coreNode;
        }

        if (target == null && legController != null)
        {
            target = legController.runTarget;
        }
    }

    private void EnsureHeldGaitForward()
    {
        if (hasHeldGaitForward)
        {
            return;
        }

        Vector3 initialForward = legController != null
            ? legController.externalGaitForward
            : player != null
                ? player.forward
                : Vector3.forward;

        initialForward = Vector3.ProjectOnPlane(initialForward, Vector3.up);
        if (initialForward.sqrMagnitude <= Epsilon && player != null)
        {
            initialForward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        }

        heldGaitForward = initialForward.sqrMagnitude > Epsilon
            ? initialForward.normalized
            : Vector3.forward;
        hasHeldGaitForward = true;
    }

    private void PushGaitForwardToController()
    {
        if (legController != null)
        {
            legController.SetExternalGaitForward(heldGaitForward, true);
        }
    }

    private Vector3 GetMoveDirectionInHeldGaitBasis(Vector2 inputAxes)
    {
        Vector3 forward = Vector3.ProjectOnPlane(heldGaitForward, Vector3.up);
        if (forward.sqrMagnitude <= Epsilon)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude <= Epsilon)
        {
            right = Vector3.right;
        }

        right.Normalize();
        return right * inputAxes.x + forward * inputAxes.y;
    }

    private static Vector2 GetWasdAxes()
    {
        Vector2 axes = Vector2.zero;

        if (IsKeyPressed(Key.W)) axes.y += 1f;
        if (IsKeyPressed(Key.S)) axes.y -= 1f;
        if (IsKeyPressed(Key.D)) axes.x += 1f;
        if (IsKeyPressed(Key.A)) axes.x -= 1f;

        return axes.sqrMagnitude > 1f ? axes.normalized : axes;
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
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            worldPosition = Vector3.zero;
            return false;
        }

        Ray mouseRay = inputCamera.ScreenPointToRay(mouse.position.ReadValue());

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
        Keyboard keyboard = Keyboard.current;
        return keyboard != null &&
               (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
    }

    private static bool IsKeyPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].isPressed;
    }
}

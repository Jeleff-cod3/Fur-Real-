using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerControllerLoose : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 5f;
    public float rotationSpeed = 5f;
    public float movementSmooth = 0.1f;
    public float jumpForce = 5f;

    [Header("Procedural Rig Movement")]
    public float proceduralRunTargetDistance = 3f;
    public float gaitTurnSpeedDegrees = 900f;

    private Rigidbody rb;
    private bool isGrounded = true;

    private Vector3 currentVelocity = Vector3.zero;
    private Vector3 moveDirection = Vector3.zero;
    private PlayerMouseAim mouseAim;
    private ProceduralPlayerRig proceduralRig;
    private Vector3 heldGaitForward = Vector3.forward;
    private bool hasHeldGaitForward;

    private void Start()
    {
        EnsureCombatSupport();
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        mouseAim = GetComponent<PlayerMouseAim>();
        proceduralRig = GetComponent<ProceduralPlayerRig>();

        if (proceduralRig != null)
        {
            proceduralRig.Configure(true);
            proceduralRig.ConfigureMovementSpeed(speed);

            if (proceduralRig.HasLegController && rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
    }

    private void Update()
    {
        if (proceduralRig != null && proceduralRig.HasLegController)
        {
            HandleProceduralRigMovement();
            HandleProceduralRigJump();
            return;
        }

        HandleMovement();
        HandleJump();
    }

    private void HandleProceduralRigMovement()
    {
        if (mouseAim == null)
        {
            mouseAim = GetComponent<PlayerMouseAim>();
        }

        EnsureHeldGaitForward();

        Vector3 aimPoint = default;
        bool hasAimPoint = mouseAim != null && mouseAim.TryGetMouseWorldPoint(out aimPoint);
        if (hasAimPoint)
        {
            proceduralRig.SetAimTarget(aimPoint);
        }

        bool shiftHeld = mouseAim != null && mouseAim.IsAimModifierPressed;
        Vector3 corePosition = proceduralRig.CoreNode.position;

        if (shiftHeld && hasAimPoint)
        {
            Vector3 toMouse = Vector3.ProjectOnPlane(aimPoint - corePosition, Vector3.up);
            if (toMouse.sqrMagnitude > 0.001f)
            {
                heldGaitForward = Vector3.RotateTowards(
                    heldGaitForward,
                    toMouse.normalized,
                    gaitTurnSpeedDegrees * Mathf.Deg2Rad * Time.deltaTime,
                    0f).normalized;
            }
        }

        proceduralRig.SetGaitForward(heldGaitForward);

        Vector2 input = GetWasdAxes();
        if (input.sqrMagnitude <= 0.001f)
        {
            proceduralRig.SetRunTarget(corePosition);
            moveDirection = Vector3.zero;
            return;
        }

        Vector3 basisForward = Vector3.ProjectOnPlane(heldGaitForward, Vector3.up);
        if (basisForward.sqrMagnitude <= 0.001f)
        {
            basisForward = Vector3.forward;
        }

        basisForward.Normalize();
        Vector3 basisRight = Vector3.Cross(Vector3.up, basisForward).normalized;
        Vector3 targetDirection = basisRight * input.x + basisForward * input.y;

        if (targetDirection.sqrMagnitude <= 0.001f)
        {
            proceduralRig.SetRunTarget(corePosition);
            return;
        }

        targetDirection.Normalize();
        moveDirection = targetDirection;
        proceduralRig.SetRunTarget(corePosition + targetDirection * proceduralRunTargetDistance);
    }

    private void HandleProceduralRigJump()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            proceduralRig.RequestJump();
        }
    }

    private void EnsureHeldGaitForward()
    {
        if (hasHeldGaitForward)
        {
            return;
        }

        Vector3 initial = proceduralRig != null
            ? proceduralRig.GaitForward
            : transform.forward;

        initial = Vector3.ProjectOnPlane(initial, Vector3.up);
        heldGaitForward = initial.sqrMagnitude > 0.001f ? initial.normalized : Vector3.forward;
        hasHeldGaitForward = true;
    }

    private void HandleMovement()
    {
        if (mouseAim == null)
        {
            mouseAim = GetComponent<PlayerMouseAim>();
        }

        Vector2 input = GetWasdAxes();
        bool isAimLocked = mouseAim != null && mouseAim.IsAimModifierPressed;
        Vector3 targetDirection = new Vector3(input.x, 0f, input.y).normalized;

        moveDirection = Vector3.SmoothDamp(moveDirection, targetDirection, ref currentVelocity, movementSmooth);

        Vector3 newPos = rb.position + moveDirection * speed * Time.deltaTime;
        rb.MovePosition(newPos);

        if (isAimLocked && mouseAim != null && mouseAim.TryGetAimDirection(out Vector3 aimDirection, false))
        {
            Quaternion targetRotation = Quaternion.LookRotation(aimDirection);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
        else if (moveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    private static Vector2 GetWasdAxes()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        return input.sqrMagnitude > 1f ? input.normalized : input;
    }

    private void EnsureCombatSupport()
    {
        if (GetComponent<PlayerHealth>() == null)
        {
            gameObject.AddComponent<PlayerHealth>();
        }

        if (GetComponent<PrototypePlayerRespawn>() == null)
        {
            gameObject.AddComponent<PrototypePlayerRespawn>();
        }

        if (GetComponent<PlayerMouseAim>() == null)
        {
            gameObject.AddComponent<PlayerMouseAim>();
        }

        if (GetComponent<PlayerWeaponPickup>() == null)
        {
            gameObject.AddComponent<PlayerWeaponPickup>();
        }

        if (GetComponent<PlayerCarryController>() == null)
        {
            gameObject.AddComponent<PlayerCarryController>();
        }

        if (GetComponent<PlayerItemPickup>() == null)
        {
            gameObject.AddComponent<PlayerItemPickup>();
        }

        if (GetComponent<PlayerCrafting>() == null)
        {
            gameObject.AddComponent<PlayerCrafting>();
        }

        if (GetComponent<PlayerCombat>() == null)
        {
            gameObject.AddComponent<PlayerCombat>();
        }

        if (GetComponent<PlayerHealthBarUI>() == null)
        {
            gameObject.AddComponent<PlayerHealthBarUI>();
        }
    }

    private void HandleJump()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y > 0.5f)
            {
                isGrounded = true;
                break;
            }
        }
    }
}

using UnityEngine;

public class PlayerCarryController : MonoBehaviour
{
    [SerializeField] private bool logBlockedPickups;

    private Object heldObject;
    private int handledInteractFrame = -1;

    public bool HasHeldObject => heldObject != null;
    public bool WasInteractHandledThisFrame => handledInteractFrame == Time.frameCount;

    public bool TryClaim(Object candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        if (heldObject == null || heldObject == candidate)
        {
            heldObject = candidate;
            return true;
        }

        if (logBlockedPickups)
        {
            Debug.Log($"Carry slot is occupied by {heldObject.name}. Cannot pick up {candidate.name}.");
        }

        return false;
    }

    public bool IsHolding(Object candidate)
    {
        return heldObject == candidate;
    }

    public void ReleaseIfMatches(Object candidate)
    {
        if (heldObject == candidate)
        {
            heldObject = null;
        }
    }

    public void MarkInteractHandled()
    {
        handledInteractFrame = Time.frameCount;
    }
}

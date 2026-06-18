using UnityEngine;

[DisallowMultipleComponent]
public class NodeState : MonoBehaviour
{
    private const float Epsilon = 0.000001f;

    [Header("Chain")]
    public NodeState next = null;

    [Tooltip("Vector from this node to next node. Magnitude is the bone length.")]
    public Vector3 Mylength = Vector3.zero;

    [Header("Pole")]
    public Transform pole;

    [Header("Solver Data")]
    public double MyChain = 0;
    public double MinChain = 0;

    [Header("Bend Settings")]
    [Min(0f)]
    public float BendWeight = 1f;

    [Range(0f, 180f)]
    public float MaxBendAngle = 120f;

    [Range(0f, 180f)]
    public float MinBendAngle = 0f;

    public float BoneLength
    {
        get { return Mylength.magnitude; }
    }

    public bool HasNext
    {
        get { return next != null; }
    }

    public bool HasValidBone
    {
        get { return next != null && Mylength.sqrMagnitude > Epsilon; }
    }

    private void Start()
    {
        InitializeLengthFromNext(false);
        ClampBendAngles();
    }

    private void OnValidate()
    {
        ClampBendAngles();
    }

    public void InitializeLengthFromNext(bool force)
    {
        if (next == null)
        {
            if (force)
            {
                Mylength = Vector3.zero;
            }

            return;
        }

        if (force || Mylength.sqrMagnitude <= Epsilon)
        {
            CaptureLengthFromCurrentPose();
        }
    }

    [ContextMenu("Capture Length From Current Pose")]
    public void CaptureLengthFromCurrentPose()
    {
        if (next == null)
        {
            Mylength = Vector3.zero;
            return;
        }

        Mylength = transform.position - next.transform.position;
    }

    public void ClampBendAngles()
    {
        MinBendAngle = Mathf.Clamp(MinBendAngle, 0f, 180f);
        MaxBendAngle = Mathf.Clamp(MaxBendAngle, 0f, 180f);

        if (MinBendAngle > MaxBendAngle)
        {
            float temporary = MinBendAngle;
            MinBendAngle = MaxBendAngle;
            MaxBendAngle = temporary;
        }

        BendWeight = Mathf.Max(0f, BendWeight);
    }
}
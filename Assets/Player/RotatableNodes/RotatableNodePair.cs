using UnityEngine;

[DefaultExecutionOrder(-75)]
public class RotatableNodePair : MonoBehaviour
{
    public enum PlaneNormalMode
    {
        WorldVector,
        DirectionTransformUp,
        DirectionTransformForward,
        DirectionTransformRight
    }

    [Header("Pair Nodes")]
    public RotatableNode[] nodes = new RotatableNode[2];

    [Header("Shared Rotation References")]
    public Transform coreNode;
    public Transform poleVector;

    [Header("Rotation Plane")]
    [Tooltip("WorldVector with Vector3.up means X/Z rotation and Y ignored.")]
    public PlaneNormalMode planeNormalMode = PlaneNormalMode.WorldVector;

    public Vector3 worldPlaneNormal = Vector3.up;

    [Tooltip("Used when Plane Normal Mode is one of the DirectionTransform modes.")]
    public Transform directionTransform;

    [Header("Initialization")]
    public bool pushSettingsOnAwake = true;
    public bool reinitializeNodesOnStart = true;

    [Header("Debug")]
    public bool debugLogging = false;

    private bool hasLoggedAwake = false;
    private bool hasLoggedStart = false;

    private void OnEnable()
    {
        Log("OnEnable", $"nodes={(nodes != null ? nodes.Length : 0)}, coreNode={(coreNode != null ? coreNode.name : "null")}, poleVector={(poleVector != null ? poleVector.name : "null")}, planeNormalMode={planeNormalMode}, pushSettingsOnAwake={pushSettingsOnAwake}, reinitializeNodesOnStart={reinitializeNodesOnStart}");
    }

    private void Awake()
    {
        if (!hasLoggedAwake)
        {
            hasLoggedAwake = true;
            Log("Awake", "Awake invoked.");
        }

        if (pushSettingsOnAwake)
        {
            Log("Awake", "pushSettingsOnAwake is enabled, pushing shared settings without reinitialization.");
            PushSharedSettingsToNodes(false);
        }
        else
        {
            Log("Awake", "pushSettingsOnAwake is disabled, skipping automatic push.");
        }
    }

    private void Start()
    {
        if (!hasLoggedStart)
        {
            hasLoggedStart = true;
            Log("Start", "Start invoked.");
        }

        if (reinitializeNodesOnStart)
        {
            Log("Start", "reinitializeNodesOnStart is enabled, pushing shared settings and reinitializing nodes.");
            PushSharedSettingsToNodes(true);
        }
        else
        {
            Log("Start", "reinitializeNodesOnStart is disabled, skipping automatic reinitialization.");
        }
    }

    public void SetPairRotationDegrees(float angleDegrees)
    {
        Log("SetPairRotationDegrees", $"angleDegrees={angleDegrees}");
        PushSharedSettingsToNodes(false);

        if (nodes == null)
        {
            return;
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] == null)
            {
                continue;
            }

            nodes[i].SetLocalRotationDegrees(angleDegrees);
            nodes[i].ApplyRotationOffset();
        }
    }

    public void PushSharedSettingsToNodes(bool reinitialize)
    {
        Vector3 planeNormal = GetPlaneNormal();
        Log("PushSharedSettingsToNodes", $"reinitialize={reinitialize}, planeNormal={planeNormal}, coreNode={(coreNode != null ? coreNode.name : "null")}, poleVector={(poleVector != null ? poleVector.name : "null")}, directionTransform={(directionTransform != null ? directionTransform.name : "null")}");

        if (nodes == null)
        {
            return;
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] == null)
            {
                continue;
            }

            nodes[i].SetSharedReferences(
                coreNode,
                poleVector,
                planeNormal,
                reinitialize
            );
        }
    }

    public Vector3 GetPlaneNormal()
    {
        Vector3 normal;

        switch (planeNormalMode)
        {
            case PlaneNormalMode.DirectionTransformUp:
                normal = directionTransform != null ? directionTransform.up : worldPlaneNormal;
                break;

            case PlaneNormalMode.DirectionTransformForward:
                normal = directionTransform != null ? directionTransform.forward : worldPlaneNormal;
                break;

            case PlaneNormalMode.DirectionTransformRight:
                normal = directionTransform != null ? directionTransform.right : worldPlaneNormal;
                break;

            case PlaneNormalMode.WorldVector:
            default:
                normal = worldPlaneNormal;
                break;
        }

        if (normal.sqrMagnitude < 0.000001f)
        {
            normal = Vector3.up;
        }

        Vector3 normalized = normal.normalized;
        Log("GetPlaneNormal", $"mode={planeNormalMode}, rawNormal={normal}, normalized={normalized}");
        return normalized;
    }

    [ContextMenu("Push Shared Settings")]
    public void EditorPushSharedSettings()
    {
        PushSharedSettingsToNodes(false);
    }

    [ContextMenu("Push Shared Settings And Reinitialize")]
    public void EditorPushSharedSettingsAndReinitialize()
    {
        PushSharedSettingsToNodes(true);
    }

    private void Log(string scope, string message)
    {
        if (!debugLogging)
        {
            return;
        }

        Debug.Log($"[RotatableNodePair:{name}] {scope} - {message}", this);
    }
}

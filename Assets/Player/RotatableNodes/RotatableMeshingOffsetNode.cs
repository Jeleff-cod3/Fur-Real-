using UnityEngine;

[DefaultExecutionOrder(-50)]
public class RotatableMeshingOffsetNode : RotatableNode
{
    private const float FakeOffsetEpsilon = 0.000001f;

    [Header("Fake Meshing Offsets")]
    [Tooltip("Fake offset list that receives per-offset rotation changes.")]
    public MeshingOffsetNode meshingOffsetNode;

    private void Reset()
    {
        meshingOffsetNode = GetComponent<MeshingOffsetNode>();
        initializeOnStart = true;
        applyEveryUpdate = true;
        useOffsetParentAsRotationCore = false;
    }

    public override void InitializeFromCurrentPose()
    {
        EnsureMeshingOffsetNode();

        if (meshingOffsetNode == null)
        {
            return;
        }

        meshingOffsetNode.ClearRotationOffsets();
    }

    public void SetFakeSharedReferences(
        Transform newCoreNode,
        Transform newPoleVector,
        Vector3 newRotationPlaneNormal,
        bool reinitialize
    )
    {
        SetSharedReferences(
            newCoreNode,
            newPoleVector,
            newRotationPlaneNormal,
            false
        );

        if (reinitialize)
        {
            InitializeFromCurrentPose();
        }
    }

    public void SetFakeRotationDegrees(float angleDegrees)
    {
        SetLocalRotationDegrees(angleDegrees);
        ApplyRotationOffset();
    }

    public override void ApplyRotationOffset()
    {
        EnsureMeshingOffsetNode();

        if (meshingOffsetNode == null)
        {
            return;
        }

        for (int i = 0; i < meshingOffsetNode.Count; i++)
        {
            Vector3 baseOffset = meshingOffsetNode.GetBaseOffset(i);
            Vector3 requiredOffset = CalculateRotationDynamicOffsetForStaticOffset(baseOffset);
            Vector3 currentOffset = meshingOffsetNode.GetRotationOffset(i);

            if ((currentOffset - requiredOffset).sqrMagnitude <= FakeOffsetEpsilon)
            {
                continue;
            }

            meshingOffsetNode.SetRotationOffset(i, requiredOffset);
        }
    }

    public MeshingOffsetNode GetMeshingOffsetNode()
    {
        EnsureMeshingOffsetNode();
        return meshingOffsetNode;
    }

    private void EnsureMeshingOffsetNode()
    {
        if (meshingOffsetNode == null)
        {
            meshingOffsetNode = GetComponent<MeshingOffsetNode>();
        }
    }
}

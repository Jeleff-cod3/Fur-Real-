using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-60)]
public class MeshingOffsetNode : MonoBehaviour
{
    [Serializable]
    public class MeshingOffsetEntry
    {
        [Tooltip("Static local/world offset from the parent node. For the usual top mesh this is X/Z, with Y left at 0.")]
        public Vector3 baseOffset;

        [Tooltip("Runtime offset written by RotatableMeshingOffsetNode.")]
        public Vector3 rotationOffset;

        public MeshingOffsetEntry(Vector3 baseOffset)
        {
            this.baseOffset = baseOffset;
            rotationOffset = Vector3.zero;
        }

        public Vector3 CurrentOffset
        {
            get { return baseOffset + rotationOffset; }
        }
    }

    [Header("Parent")]
    [Tooltip("World parent/core used as the center for all fake meshing points. If empty, this transform is used.")]
    public Transform parentNode;

    [Header("Fake Meshing Offsets")]
    public List<MeshingOffsetEntry> offsets = new List<MeshingOffsetEntry>()
    {
        new MeshingOffsetEntry(new Vector3(1f, 0f, 0f)),
        new MeshingOffsetEntry(new Vector3(-1f, 0f, 0f)),
        new MeshingOffsetEntry(new Vector3(0f, 0f, 1f)),
        new MeshingOffsetEntry(new Vector3(0f, 0f, -1f))
    };

    [Header("Debug")]
    public bool debugLogging = false;

    public int Count
    {
        get { return offsets != null ? offsets.Count : 0; }
    }

    private void Reset()
    {
        parentNode = transform;
        EnsureDefaultOffsets();
    }

    private void OnValidate()
    {
        EnsureDefaultOffsets();
    }

    [ContextMenu("Reset To Default Four Offsets")]
    public void ResetToDefaultOffsets()
    {
        if (offsets == null)
        {
            offsets = new List<MeshingOffsetEntry>();
        }

        offsets.Clear();
        offsets.Add(new MeshingOffsetEntry(new Vector3(1f, 0f, 0f)));
        offsets.Add(new MeshingOffsetEntry(new Vector3(-1f, 0f, 0f)));
        offsets.Add(new MeshingOffsetEntry(new Vector3(0f, 0f, 1f)));
        offsets.Add(new MeshingOffsetEntry(new Vector3(0f, 0f, -1f)));
    }

    public Transform ResolveParentTransform()
    {
        return parentNode != null ? parentNode : transform;
    }

    public Vector3 GetParentWorldPosition()
    {
        Transform resolvedParent = ResolveParentTransform();
        return resolvedParent != null ? resolvedParent.position : Vector3.zero;
    }

    public Vector3 GetBaseOffset(int index)
    {
        if (!IsValidIndex(index))
        {
            return Vector3.zero;
        }

        return offsets[index].baseOffset;
    }

    public Vector3 GetRotationOffset(int index)
    {
        if (!IsValidIndex(index))
        {
            return Vector3.zero;
        }

        return offsets[index].rotationOffset;
    }

    public Vector3 GetCurrentOffset(int index)
    {
        if (!IsValidIndex(index))
        {
            return Vector3.zero;
        }

        return offsets[index].CurrentOffset;
    }

    public Vector3 GetWorldPosition(int index)
    {
        return GetParentWorldPosition() + GetCurrentOffset(index);
    }

    public void SetRotationOffset(int index, Vector3 value)
    {
        if (!IsValidIndex(index))
        {
            return;
        }

        offsets[index].rotationOffset = value;
        Log("SetRotationOffset", "index=" + index + ", value=" + value);
    }

    public void ClearRotationOffsets()
    {
        if (offsets == null)
        {
            return;
        }

        for (int i = 0; i < offsets.Count; i++)
        {
            offsets[i].rotationOffset = Vector3.zero;
        }
    }

    public void CopyWorldPositionsTo(List<Vector3> output)
    {
        if (output == null || offsets == null)
        {
            return;
        }

        for (int i = 0; i < offsets.Count; i++)
        {
            output.Add(GetWorldPosition(i));
        }
    }

    private bool IsValidIndex(int index)
    {
        return offsets != null && index >= 0 && index < offsets.Count && offsets[index] != null;
    }

    private void EnsureDefaultOffsets()
    {
        if (offsets == null)
        {
            offsets = new List<MeshingOffsetEntry>();
        }

        if (offsets.Count == 0)
        {
            ResetToDefaultOffsets();
        }
    }

    private void Log(string scope, string message)
    {
        if (!debugLogging)
        {
            return;
        }

        Debug.Log("[MeshingOffsetNode:" + name + "] " + scope + " - " + message, this);
    }
}

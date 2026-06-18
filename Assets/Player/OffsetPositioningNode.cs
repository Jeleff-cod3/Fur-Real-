using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(260)]
public class OffsetPositioningNode : MonoBehaviour
{
    [Header("Debug")]
    public bool debugLogging = true;

    [Serializable]
    public struct DynamicOffsetEntry
    {
        public int id;
        public Vector3 value;

        public DynamicOffsetEntry(int id, Vector3 value)
        {
            this.id = id;
            this.value = value;
        }
    }

    [Header("Parent")]
    public Transform parentNode;

    [Header("Static Offset")]
    [Tooltip("World-space offset from the parent node position.")]
    public Vector3 staticOffset;

    [Tooltip("Legacy/manual capture flag. Static offset is no longer auto-overwritten on Awake.")]
    public bool captureStaticOffsetOnAwake = false;

    [Header("Dynamic Offsets")]
    [SerializeField]
    private List<DynamicOffsetEntry> dynamicOffsets = new List<DynamicOffsetEntry>();

    [Header("Apply")]
    [Tooltip("LateUpdate is safer because behavior scripts can write offsets during Update first.")]
    public bool applyInLateUpdate = true;

    public IReadOnlyList<DynamicOffsetEntry> DynamicOffsets => dynamicOffsets;

    private bool hasLoggedUpdate = false;
    private bool hasLoggedLateUpdate = false;
    private Vector3 lastLoggedAppliedPosition;
    private bool hasLoggedAppliedPosition = false;

    private void OnEnable()
    {
        Log("OnEnable", $"parent={(parentNode != null ? parentNode.name : "null")}, staticOffset={staticOffset}, dynamicOffsets={dynamicOffsets.Count}, applyInLateUpdate={applyInLateUpdate}, captureStaticOffsetOnAwake={captureStaticOffsetOnAwake}");
    }

    private void Awake()
    {
        Log("Awake", $"parent={(parentNode != null ? parentNode.name : "null")}, staticOffset={staticOffset}, dynamicOffsets={dynamicOffsets.Count}");

        if (captureStaticOffsetOnAwake)
        {
            Log("Awake", "captureStaticOffsetOnAwake is enabled, but automatic capture is disabled to preserve the authored staticOffset.");
        }

        MergeDuplicateOffsetIds();
        Log("Awake", $"after merge dynamicOffsets={dynamicOffsets.Count}");
    }

    private void Update()
    {
        if (!hasLoggedUpdate)
        {
            hasLoggedUpdate = true;
            Log("Update", $"entered Update, applyInLateUpdate={applyInLateUpdate}");
        }

        if (!applyInLateUpdate)
        {
            ApplyPosition();
        }
    }

    private void LateUpdate()
    {
        if (!hasLoggedLateUpdate)
        {
            hasLoggedLateUpdate = true;
            Log("LateUpdate", $"entered LateUpdate, applyInLateUpdate={applyInLateUpdate}");
        }

        if (applyInLateUpdate)
        {
            ApplyPosition();
        }
    }

    [ContextMenu("Capture Static Offset From Current Position")]
    public void CaptureStaticOffsetFromCurrentPosition()
    {
        Vector3 parentPosition = parentNode != null ? parentNode.position : Vector3.zero;
        staticOffset = transform.position - parentPosition;
        Log("CaptureStaticOffsetFromCurrentPosition", $"parentPosition={parentPosition}, transformPosition={transform.position}, capturedStaticOffset={staticOffset}");
    }

    public void ApplyPosition()
    {
        Vector3 finalWorldPosition = GetFinalWorldPosition();
        transform.position = finalWorldPosition;

        if (!hasLoggedAppliedPosition || (lastLoggedAppliedPosition - finalWorldPosition).sqrMagnitude > 0.000001f)
        {
            hasLoggedAppliedPosition = true;
            lastLoggedAppliedPosition = finalWorldPosition;
            Log("ApplyPosition", $"appliedPosition={finalWorldPosition}, parentPosition={GetParentWorldPosition()}, appliedStaticOffset={GetAppliedStaticOffset()}, totalDynamicOffset={GetTotalDynamicOffset()}");
        }
    }

    public Vector3 GetFinalWorldPosition()
    {
        return GetParentWorldPosition() + GetAppliedStaticOffset() + GetTotalDynamicOffset();
    }

    public Vector3 GetParentWorldPosition()
    {
        return parentNode != null ? parentNode.position : Vector3.zero;
    }

    public Vector3 GetTotalDynamicOffset()
    {
        Vector3 total = Vector3.zero;

        for (int i = 0; i < dynamicOffsets.Count; i++)
        {
            total += dynamicOffsets[i].value;
        }

        return total;
    }

    public Vector3 GetTotalDynamicOffsetExcluding(int excludedId)
    {
        Vector3 total = Vector3.zero;

        for (int i = 0; i < dynamicOffsets.Count; i++)
        {
            if (dynamicOffsets[i].id == excludedId)
            {
                continue;
            }

            total += dynamicOffsets[i].value;
        }

        return total;
    }

    public Vector3 GetWorldPositionWithoutDynamicOffset(int excludedId)
    {
        return GetParentWorldPosition()
               + GetAppliedStaticOffset()
               + GetTotalDynamicOffsetExcluding(excludedId);
    }

    public Vector3 GetAppliedStaticOffset()
    {
        return staticOffset;
    }

    public Vector3 CalculateDynamicOffsetForDesiredWorldPosition(int id, Vector3 desiredWorldPosition)
    {
        Vector3 positionWithoutThisOffset = GetWorldPositionWithoutDynamicOffset(id);
        return desiredWorldPosition - positionWithoutThisOffset;
    }

    public void SetDynamicOffsetToReachWorldPosition(int id, Vector3 desiredWorldPosition)
    {
        Vector3 requiredOffset = CalculateDynamicOffsetForDesiredWorldPosition(id, desiredWorldPosition);
        SetDynamicOffset(id, requiredOffset);
    }

    public void SetDynamicOffset(int id, Vector3 value)
    {
        int index = FindDynamicOffsetIndex(id);

        if (index >= 0)
        {
            DynamicOffsetEntry entry = dynamicOffsets[index];
            entry.value = value;
            dynamicOffsets[index] = entry;
        }
        else
        {
            dynamicOffsets.Add(new DynamicOffsetEntry(id, value));
        }
    }

    public void AddToDynamicOffset(int id, Vector3 addedValue)
    {
        Vector3 currentValue = GetDynamicOffset(id);
        SetDynamicOffset(id, currentValue + addedValue);
    }

    public Vector3 GetDynamicOffset(int id)
    {
        int index = FindDynamicOffsetIndex(id);

        if (index < 0)
        {
            return Vector3.zero;
        }

        return dynamicOffsets[index].value;
    }

    public bool HasDynamicOffset(int id)
    {
        return FindDynamicOffsetIndex(id) >= 0;
    }

    public bool RemoveDynamicOffset(int id)
    {
        int index = FindDynamicOffsetIndex(id);

        if (index < 0)
        {
            return false;
        }

        dynamicOffsets.RemoveAt(index);
        return true;
    }

    public void ClearDynamicOffsets()
    {
        dynamicOffsets.Clear();
    }

    public void ClearDynamicOffsetValue(int id)
    {
        SetDynamicOffset(id, Vector3.zero);
    }

    private int FindDynamicOffsetIndex(int id)
    {
        for (int i = 0; i < dynamicOffsets.Count; i++)
        {
            if (dynamicOffsets[i].id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void MergeDuplicateOffsetIds()
    {
        for (int i = 0; i < dynamicOffsets.Count; i++)
        {
            DynamicOffsetEntry baseEntry = dynamicOffsets[i];

            for (int j = dynamicOffsets.Count - 1; j > i; j--)
            {
                if (dynamicOffsets[j].id != baseEntry.id)
                {
                    continue;
                }

                baseEntry.value += dynamicOffsets[j].value;
                dynamicOffsets.RemoveAt(j);
            }

            dynamicOffsets[i] = baseEntry;
        }
    }

    private void Log(string scope, string message)
    {
        if (!debugLogging)
        {
            return;
        }

        Debug.Log($"[OffsetPositioningNode:{name}] {scope} - {message}", this);
    }
}

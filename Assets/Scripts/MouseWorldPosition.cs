using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

[DefaultExecutionOrder(-250)]
public class MouseWorldPosition : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private Transform marker;
    [SerializeField] private RawImage renderTextureRawImage;
    [SerializeField] private RectTransform renderTextureDisplayRect;

    [Header("Raycast")]
    [SerializeField] private float maxRayDistance = 1000f;
    [SerializeField] private float markerHeightOffset = 0.03f;
    [SerializeField] private float markerScale = 1f;
    [SerializeField] private bool showDebugRay = false;
    [SerializeField] private bool createMarkerIfMissing = true;

    public static MouseWorldPosition Instance { get; private set; }

    public Vector3 WorldPosition { get; private set; }
    public bool HasHitGround { get; private set; }

    private RaycastHit latestGroundHit;
    private int lastRefreshFrame = -1;
    private Mesh generatedMarkerMesh;
    private Material generatedMarkerMaterial;

    private void Reset()
    {
        worldCamera = GetComponent<Camera>();
        AssignDefaultGroundLayer();
        ResolveRenderTextureReferences();
    }

    private void Awake()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }

        ResolveReferences();
        SetMarkerVisible(false);
    }

    private void OnEnable()
    {
        lastRefreshFrame = -1;
        ResolveReferences();
        SetMarkerVisible(false);
    }

    private void OnDisable()
    {
        SetMarkerVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        DestroyGeneratedAssets();
    }

    private void Update()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (lastRefreshFrame == Time.frameCount)
        {
            return;
        }

        lastRefreshFrame = Time.frameCount;
        ResolveReferences();

        if (!TryBuildWorldRay(out Ray worldRay))
        {
            ClearGroundHit();
            return;
        }

        int raycastMask = groundLayer.value != 0
            ? groundLayer.value
            : Physics.DefaultRaycastLayers;

        if (Physics.Raycast(
                worldRay,
                out RaycastHit hit,
                maxRayDistance,
                raycastMask,
                QueryTriggerInteraction.Ignore))
        {
            latestGroundHit = hit;
            WorldPosition = hit.point;
            HasHitGround = true;
            UpdateMarker(hit);

            if (showDebugRay)
            {
                Debug.DrawLine(worldRay.origin, hit.point, Color.white);
            }

            return;
        }

        ClearGroundHit();

        if (showDebugRay)
        {
            Debug.DrawRay(worldRay.origin, worldRay.direction * maxRayDistance, Color.gray);
        }
    }

    public bool TryGetGroundHit(out RaycastHit hit)
    {
        Refresh();
        hit = latestGroundHit;
        return HasHitGround;
    }

    private void ResolveReferences()
    {
        if (worldCamera == null)
        {
            worldCamera = ResolveWorldCamera();
        }

        AssignDefaultGroundLayer();
        ResolveRenderTextureReferences();

        if (marker == null && createMarkerIfMissing)
        {
            marker = CreateGeneratedMarker();
        }
    }

    private void AssignDefaultGroundLayer()
    {
        if (groundLayer.value != 0)
        {
            return;
        }

        int groundLayerIndex = LayerMask.NameToLayer("Ground");

        if (groundLayerIndex >= 0)
        {
            groundLayer = 1 << groundLayerIndex;
        }
    }

    private void ResolveRenderTextureReferences()
    {
        if (worldCamera == null)
        {
            return;
        }

        if (worldCamera.targetTexture == null)
        {
            if (renderTextureDisplayRect == null && renderTextureRawImage != null)
            {
                renderTextureDisplayRect = renderTextureRawImage.rectTransform;
            }

            return;
        }

        if (renderTextureRawImage == null)
        {
            renderTextureRawImage = FindMatchingRawImage(worldCamera.targetTexture);
        }

        if (renderTextureDisplayRect == null)
        {
            renderTextureDisplayRect = renderTextureRawImage != null
                ? renderTextureRawImage.rectTransform
                : null;
        }
    }

    private Camera ResolveWorldCamera()
    {
        Camera selfCamera = GetComponent<Camera>();
        if (selfCamera != null && selfCamera.enabled)
        {
            return selfCamera;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.enabled)
        {
            return mainCamera;
        }

        Camera[] cameras = FindObjectsByType<Camera>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        Camera targetTextureCamera = null;

        foreach (Camera candidate in cameras)
        {
            if (candidate == null || !candidate.enabled || candidate.cameraType != CameraType.Game)
            {
                continue;
            }

            if (candidate.targetTexture != null)
            {
                targetTextureCamera = candidate;
                break;
            }
        }

        if (targetTextureCamera != null)
        {
            return targetTextureCamera;
        }

        foreach (Camera candidate in cameras)
        {
            if (candidate != null && candidate.enabled && candidate.cameraType == CameraType.Game)
            {
                return candidate;
            }
        }

        return null;
    }

    private RawImage FindMatchingRawImage(RenderTexture targetTexture)
    {
        if (targetTexture == null)
        {
            return null;
        }

        RawImage[] rawImages = FindObjectsByType<RawImage>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (RawImage candidate in rawImages)
        {
            if (candidate != null && candidate.texture == targetTexture)
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryBuildWorldRay(out Ray worldRay)
    {
        worldRay = default;

        if (worldCamera == null || Mouse.current == null)
        {
            return false;
        }

        Vector2 screenMousePosition = Mouse.current.position.ReadValue();

        if (worldCamera.targetTexture == null)
        {
            worldRay = worldCamera.ScreenPointToRay(screenMousePosition);
            return true;
        }

        if (!TryConvertScreenPointToWorldCameraPoint(screenMousePosition, out Vector2 cameraPoint))
        {
            return false;
        }

        worldRay = worldCamera.ScreenPointToRay(cameraPoint);
        return true;
    }

    private bool TryConvertScreenPointToWorldCameraPoint(
        Vector2 screenPoint,
        out Vector2 cameraPoint)
    {
        cameraPoint = Vector2.zero;

        if (worldCamera == null)
        {
            return false;
        }

        if (worldCamera.targetTexture == null)
        {
            cameraPoint = screenPoint;
            return true;
        }

        RectTransform displayRect = renderTextureDisplayRect;

        if (displayRect == null && renderTextureRawImage != null)
        {
            displayRect = renderTextureRawImage.rectTransform;
        }

        if (displayRect != null)
        {
            Camera uiCamera = ResolveUiCamera(displayRect);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    displayRect,
                    screenPoint,
                    uiCamera,
                    out Vector2 localPoint))
            {
                return false;
            }

            Rect rect = displayRect.rect;

            if (!rect.Contains(localPoint))
            {
                return false;
            }

            Rect uvRect = renderTextureRawImage != null
                ? renderTextureRawImage.uvRect
                : new Rect(0f, 0f, 1f, 1f);

            float normalizedX = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
            float normalizedY = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);
            float uvX = Mathf.Lerp(uvRect.xMin, uvRect.xMax, normalizedX);
            float uvY = Mathf.Lerp(uvRect.yMin, uvRect.yMax, normalizedY);

            cameraPoint = new Vector2(
                uvX * GetRenderWidth(),
                uvY * GetRenderHeight());

            return true;
        }

        float normalizedScreenX = Screen.width > 0
            ? Mathf.Clamp01(screenPoint.x / Screen.width)
            : 0f;
        float normalizedScreenY = Screen.height > 0
            ? Mathf.Clamp01(screenPoint.y / Screen.height)
            : 0f;

        cameraPoint = new Vector2(
            normalizedScreenX * GetRenderWidth(),
            normalizedScreenY * GetRenderHeight());

        return true;
    }

    private Camera ResolveUiCamera(RectTransform displayRect)
    {
        Canvas canvas = displayRect.GetComponentInParent<Canvas>();

        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        if (canvas.worldCamera != null)
        {
            return canvas.worldCamera;
        }

        return Camera.main;
    }

    private float GetRenderWidth()
    {
        if (worldCamera != null && worldCamera.targetTexture != null)
        {
            return worldCamera.targetTexture.width;
        }

        return worldCamera != null ? Mathf.Max(1f, worldCamera.pixelWidth) : 1f;
    }

    private float GetRenderHeight()
    {
        if (worldCamera != null && worldCamera.targetTexture != null)
        {
            return worldCamera.targetTexture.height;
        }

        return worldCamera != null ? Mathf.Max(1f, worldCamera.pixelHeight) : 1f;
    }

    private void ClearGroundHit()
    {
        HasHitGround = false;
        latestGroundHit = default;
        SetMarkerVisible(false);
    }

    private void UpdateMarker(RaycastHit hit)
    {
        if (marker == null)
        {
            return;
        }

        marker.position = hit.point + hit.normal * markerHeightOffset;
        marker.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
        marker.localScale = Vector3.one * Mathf.Max(0.01f, markerScale);
        SetMarkerVisible(true);
    }

    private void SetMarkerVisible(bool isVisible)
    {
        if (marker == null || marker.gameObject.activeSelf == isVisible)
        {
            return;
        }

        marker.gameObject.SetActive(isVisible);
    }

    private Transform CreateGeneratedMarker()
    {
        GameObject markerObject = new GameObject("Mouse Ground Marker");
        markerObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        markerObject.transform.SetParent(transform, false);

        MeshFilter meshFilter = markerObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = markerObject.AddComponent<MeshRenderer>();

        generatedMarkerMesh = CreateRingMesh(48, 0.7f, 1f);
        generatedMarkerMaterial = CreateMarkerMaterial();

        meshFilter.sharedMesh = generatedMarkerMesh;
        meshRenderer.sharedMaterial = generatedMarkerMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        markerObject.SetActive(false);
        return markerObject.transform;
    }

    private Mesh CreateRingMesh(int segments, float innerRadius, float outerRadius)
    {
        Mesh mesh = new Mesh
        {
            name = "MouseGroundRing"
        };

        Vector3[] vertices = new Vector3[segments * 2];
        Vector2[] uv = new Vector2[segments * 2];
        int[] triangles = new int[segments * 6];

        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            float sin = Mathf.Sin(angle);
            float cos = Mathf.Cos(angle);

            int innerIndex = i * 2;
            int outerIndex = innerIndex + 1;

            vertices[innerIndex] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
            vertices[outerIndex] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);

            uv[innerIndex] = new Vector2(t, 0f);
            uv[outerIndex] = new Vector2(t, 1f);

            int nextInnerIndex = ((i + 1) % segments) * 2;
            int nextOuterIndex = nextInnerIndex + 1;
            int triangleIndex = i * 6;

            triangles[triangleIndex] = innerIndex;
            triangles[triangleIndex + 1] = nextOuterIndex;
            triangles[triangleIndex + 2] = outerIndex;
            triangles[triangleIndex + 3] = innerIndex;
            triangles[triangleIndex + 4] = nextInnerIndex;
            triangles[triangleIndex + 5] = nextOuterIndex;
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private Material CreateMarkerMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader)
        {
            name = "MouseGroundMarkerMaterial"
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        return material;
    }

    private void DestroyGeneratedAssets()
    {
        DestroyGeneratedAsset(generatedMarkerMesh);
        DestroyGeneratedAsset(generatedMarkerMaterial);
    }

    private void DestroyGeneratedAsset(Object asset)
    {
        if (asset == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(asset);
            return;
        }

        DestroyImmediate(asset);
    }
}

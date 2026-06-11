using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public class PlayerHealthBarUI : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private PlayerHealth target;

    [Header("Layout")]
    [SerializeField] private Vector2 anchorMin = new Vector2(0.02f, 0.93f);
    [SerializeField] private Vector2 anchorMax = new Vector2(0.29f, 0.985f);
    [SerializeField] private Vector2 anchoredOffset = Vector2.zero;
    [SerializeField] private Vector2 minSize = new Vector2(280f, 52f);
    [SerializeField] private float innerPadding = 4f;
    [SerializeField] private bool useDedicatedOverlayCanvas = true;

    [Header("Resolution")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] [Range(0f, 1f)] private float widthHeightMatch = 0.5f;

    [Header("Animation")]
    [SerializeField] private float healthSlideSpeed = 2.2f;
    [SerializeField] private float damageHoldDuration = 0.12f;
    [SerializeField] private float damageBurnSpeed = 0.9f;
    [SerializeField] private float damageFadeSpeed = 2.8f;

    [Header("Text")]
    [SerializeField] private string titleText = "PLAYER";
    [SerializeField] private int titleFontSize = 14;
    [SerializeField] private int valueFontSize = 16;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.06f, 0.06f, 0.07f, 0.9f);
    [SerializeField] private Color fillColor = new Color(0.85f, 0.93f, 0.92f, 0.98f);
    [SerializeField] private Color damageColor = new Color(0.96f, 0.32f, 0.26f, 0.95f);
    [SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.16f);
    [SerializeField] private Color titleColor = new Color(1f, 1f, 1f, 0.82f);
    [SerializeField] private Color valueColor = Color.white;

    private RectTransform root;
    private RectTransform contentRoot;
    private RectTransform fillRect;
    private RectTransform damageRect;
    private Image fillImage;
    private Image damageImage;
    private Text titleLabel;
    private Text valueLabel;
    private PlayerHealth boundTarget;
    private bool isSubscribed;
    private int lastKnownCurrentHealth = -1;
    private int lastKnownMaxHealth = -1;
    private float targetPercent = 1f;
    private float displayedPercent = 1f;
    private float damageGhostPercent = 1f;
    private float damageHoldTimer;
    private float damageAlpha;
    private Canvas uiCanvas;
    private static Canvas sharedOverlayCanvas;
    private static Font sharedFont;

    private void Awake()
    {
        if (target == null)
        {
            target = GetComponent<PlayerHealth>();
        }

        BuildBar();
    }

    private void OnEnable()
    {
        BuildBar();
        BindTarget(target != null ? target : GetComponent<PlayerHealth>());
    }

    private void OnDisable()
    {
        Unsubscribe();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        Unsubscribe();

        if (root != null)
        {
            Destroy(root.gameObject);
            root = null;
        }
    }

    private void Update()
    {
        if (boundTarget == null)
        {
            BindTarget(target != null ? target : GetComponent<PlayerHealth>());
        }

        if (boundTarget != null &&
            (lastKnownCurrentHealth != boundTarget.CurrentHealth || lastKnownMaxHealth != boundTarget.MaxHealth))
        {
            ApplyHealthSnapshot(boundTarget.CurrentHealth, boundTarget.MaxHealth);
        }

        AnimateBar();
    }

    private void BindTarget(PlayerHealth newTarget)
    {
        if (boundTarget == newTarget)
        {
            return;
        }

        Unsubscribe();
        boundTarget = newTarget;

        if (boundTarget == null)
        {
            SetVisible(false);
            return;
        }

        boundTarget.HealthChanged += HandleHealthChanged;
        isSubscribed = true;
        SetVisible(true);
        ApplyHealthSnapshot(boundTarget.CurrentHealth, boundTarget.MaxHealth, true);
    }

    private void Unsubscribe()
    {
        if (boundTarget != null && isSubscribed)
        {
            boundTarget.HealthChanged -= HandleHealthChanged;
        }

        isSubscribed = false;
        boundTarget = null;
    }

    private void HandleHealthChanged(int current, int max)
    {
        ApplyHealthSnapshot(current, max);
    }

    private void ApplyHealthSnapshot(int current, int max, bool snapImmediately = false)
    {
        float nextPercent = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

        if (snapImmediately || lastKnownCurrentHealth < 0 || nextPercent > targetPercent)
        {
            targetPercent = nextPercent;
            displayedPercent = nextPercent;
            damageGhostPercent = nextPercent;
            damageHoldTimer = 0f;
            damageAlpha = 0f;
            lastKnownCurrentHealth = current;
            lastKnownMaxHealth = max;
            RefreshVisuals();
            return;
        }

        if (nextPercent < targetPercent)
        {
            damageGhostPercent = Mathf.Max(damageGhostPercent, displayedPercent, targetPercent);
            damageHoldTimer = damageHoldDuration;
            damageAlpha = 1f;
        }

        targetPercent = nextPercent;
        lastKnownCurrentHealth = current;
        lastKnownMaxHealth = max;
        RefreshVisuals();
    }

    private void AnimateBar()
    {
        bool changed = false;

        if (!Mathf.Approximately(displayedPercent, targetPercent))
        {
            displayedPercent = Mathf.MoveTowards(displayedPercent, targetPercent, healthSlideSpeed * Time.unscaledDeltaTime);
            changed = true;
        }

        if (damageHoldTimer > 0f)
        {
            damageHoldTimer = Mathf.Max(0f, damageHoldTimer - Time.unscaledDeltaTime);
            damageAlpha = 1f;
            changed = true;
        }
        else if (damageGhostPercent > displayedPercent)
        {
            damageGhostPercent = Mathf.MoveTowards(damageGhostPercent, displayedPercent, damageBurnSpeed * Time.unscaledDeltaTime);
            damageAlpha = Mathf.MoveTowards(damageAlpha, 0f, damageFadeSpeed * Time.unscaledDeltaTime);
            changed = true;
        }
        else if (!Mathf.Approximately(damageGhostPercent, displayedPercent))
        {
            damageGhostPercent = displayedPercent;
            damageAlpha = 0f;
            changed = true;
        }
        else if (damageAlpha > 0f)
        {
            damageAlpha = 0f;
            changed = true;
        }

        if (changed)
        {
            RefreshVisuals();
        }
    }

    private void BuildBar()
    {
        if (root != null)
        {
            return;
        }

        uiCanvas = ResolveCanvas();
        Transform parent = uiCanvas != null ? uiCanvas.transform : transform;

        GameObject rootObject = new GameObject("Player HP Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        rootObject.transform.SetParent(parent, false);
        root = rootObject.GetComponent<RectTransform>();
        root.anchorMin = anchorMin;
        root.anchorMax = anchorMax;
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = anchoredOffset;
        root.sizeDelta = minSize;
        rootObject.transform.SetAsLastSibling();

        Image background = rootObject.GetComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;

        Outline outline = rootObject.GetComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1f, -1f);

        GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        titleObject.transform.SetParent(root, false);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 0.62f);
        titleRect.anchorMax = new Vector2(0.55f, 1f);
        titleRect.offsetMin = new Vector2(10f, -2f);
        titleRect.offsetMax = new Vector2(-6f, -4f);

        titleLabel = titleObject.GetComponent<Text>();
        titleLabel.font = GetUiFont();
        titleLabel.fontSize = titleFontSize;
        titleLabel.fontStyle = FontStyle.Bold;
        titleLabel.alignment = TextAnchor.MiddleLeft;
        titleLabel.color = titleColor;
        titleLabel.raycastTarget = false;
        titleLabel.text = titleText;

        GameObject valueObject = new GameObject("Value", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        valueObject.transform.SetParent(root, false);
        RectTransform valueRect = valueObject.GetComponent<RectTransform>();
        valueRect.anchorMin = new Vector2(0.55f, 0.62f);
        valueRect.anchorMax = new Vector2(1f, 1f);
        valueRect.offsetMin = new Vector2(6f, -2f);
        valueRect.offsetMax = new Vector2(-10f, -4f);

        valueLabel = valueObject.GetComponent<Text>();
        valueLabel.font = GetUiFont();
        valueLabel.fontSize = valueFontSize;
        valueLabel.fontStyle = FontStyle.Bold;
        valueLabel.alignment = TextAnchor.MiddleRight;
        valueLabel.color = valueColor;
        valueLabel.raycastTarget = false;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(root, false);
        contentRoot = contentObject.GetComponent<RectTransform>();
        contentRoot.anchorMin = new Vector2(0f, 0f);
        contentRoot.anchorMax = new Vector2(1f, 0.58f);
        contentRoot.offsetMin = new Vector2(innerPadding + 6f, innerPadding + 6f);
        contentRoot.offsetMax = new Vector2(-(innerPadding + 6f), -(innerPadding + 2f));

        GameObject backingObject = new GameObject("Backing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backingObject.transform.SetParent(contentRoot, false);
        RectTransform backingRect = backingObject.GetComponent<RectTransform>();
        backingRect.anchorMin = Vector2.zero;
        backingRect.anchorMax = Vector2.one;
        backingRect.offsetMin = Vector2.zero;
        backingRect.offsetMax = Vector2.zero;

        Image backingImage = backingObject.GetComponent<Image>();
        backingImage.color = new Color(0f, 0f, 0f, 0.3f);
        backingImage.raycastTarget = false;

        damageImage = CreateFillImage("Damage Ghost", contentRoot, damageColor);
        fillImage = CreateFillImage("Main Fill", contentRoot, fillColor);
        damageRect = damageImage.rectTransform;
        fillRect = fillImage.rectTransform;

        RefreshVisuals();
    }

    private Canvas ResolveCanvas()
    {
        if (!useDedicatedOverlayCanvas)
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                return parentCanvas;
            }
        }

        if (sharedOverlayCanvas != null)
        {
            return sharedOverlayCanvas;
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Canvas canvas in canvases)
        {
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                sharedOverlayCanvas = canvas;
                EnsureCanvasScaler(sharedOverlayCanvas);
                return sharedOverlayCanvas;
            }
        }

        GameObject canvasObject = new GameObject("Player HP Overlay Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas createdCanvas = canvasObject.GetComponent<Canvas>();
        createdCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        createdCanvas.overrideSorting = true;
        createdCanvas.sortingOrder = 520;
        EnsureCanvasScaler(createdCanvas);
        DontDestroyOnLoad(canvasObject);
        sharedOverlayCanvas = createdCanvas;
        return sharedOverlayCanvas;
    }

    private void EnsureCanvasScaler(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.matchWidthOrHeight = widthHeightMatch;
    }

    private static Image CreateFillImage(string name, Transform parent, Color color)
    {
        GameObject fillObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObject.transform.SetParent(parent, false);
        RectTransform rect = fillObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = fillObject.GetComponent<Image>();
        image.color = color;
        image.type = Image.Type.Simple;
        image.raycastTarget = false;
        return image;
    }

    private void RefreshVisuals()
    {
        if (fillRect == null || damageRect == null || fillImage == null || damageImage == null)
        {
            return;
        }

        float clampedDisplayed = Mathf.Clamp01(displayedPercent);
        float clampedGhost = Mathf.Clamp01(Mathf.Max(damageGhostPercent, clampedDisplayed));

        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(clampedDisplayed, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        damageRect.anchorMin = new Vector2(clampedDisplayed, 0f);
        damageRect.anchorMax = new Vector2(clampedGhost, 1f);
        damageRect.offsetMin = Vector2.zero;
        damageRect.offsetMax = Vector2.zero;

        fillImage.color = fillColor;
        damageImage.color = new Color(damageColor.r, damageColor.g, damageColor.b, Mathf.Clamp01(damageColor.a * damageAlpha));

        if (titleLabel != null)
        {
            titleLabel.text = titleText;
        }

        if (valueLabel != null)
        {
            int safeCurrent = Mathf.Max(0, lastKnownCurrentHealth);
            int safeMax = Mathf.Max(1, lastKnownMaxHealth);
            valueLabel.text = $"{safeCurrent} / {safeMax}";
        }
    }

    private void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.gameObject.SetActive(visible);
        }
    }

    private static Font GetUiFont()
    {
        if (sharedFont == null)
        {
            sharedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (sharedFont == null)
            {
                sharedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
        }

        return sharedFont;
    }
}

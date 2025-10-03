using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// レガシーUI Textを使用した日本語対応のSetupStatusUI
/// TextMeshProで日本語表示に問題がある場合の代替案
/// </summary>
public class SetupStatusUILegacy : MonoBehaviour
{
    [Header("UI References")]
    public Canvas statusCanvas;
    public Text statusText;  // UI Text (レガシー)
    public Text detailsText; // UI Text (レガシー)
    public Button dismissButton;

    private GameObject canvasGO;
    private GameObject panelGO;
    private GameObject statusTextGO;
    private GameObject detailsTextGO;

    [Header("Auto-Hide Settings")]
    public float autoHideDelay = 0.5f;
    public bool hideOnFirstFrame = true;

    private static SetupStatusUILegacy instance;
    private List<string> statusMessages = new List<string>();
    private List<SensorDevice> deviceStatusList = new List<SensorDevice>();
    private bool firstFrameProcessed = false;
    private float hideTimer = 0f;
    private bool shouldAutoHide = false;

    public static SetupStatusUILegacy Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<SetupStatusUILegacy>();
                if (instance == null)
                {
                    GameObject go = new GameObject("SetupStatusUILegacy");
                    instance = go.AddComponent<SetupStatusUILegacy>();
                }
            }
            return instance;
        }
    }

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);

            if (statusCanvas == null)
            {
                CreateUI();
            }
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        UpdateDisplay();
    }

    void Update()
    {
        if (shouldAutoHide)
        {
            hideTimer += Time.deltaTime;
            if (hideTimer >= autoHideDelay)
            {
                HideUI();
            }
        }
    }

    void CreateUI()
    {
        // Create Canvas
        canvasGO = new GameObject("StatusCanvasLegacy");
        canvasGO.transform.SetParent(transform);
        statusCanvas = canvasGO.AddComponent<Canvas>();
        statusCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        statusCanvas.sortingOrder = 1000;

        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Background Panel
        panelGO = new GameObject("StatusPanel");
        panelGO.transform.SetParent(statusCanvas.transform);
        Image panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f);

        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Status Text (UI Text)
        statusTextGO = new GameObject("StatusTextLegacy");
        statusTextGO.transform.SetParent(panelGO.transform);
        statusText = statusTextGO.AddComponent<Text>();
        statusText.text = "Initializing...";
        statusText.fontSize = 24;
        statusText.color = Color.white;
        statusText.alignment = TextAnchor.MiddleCenter;

        // Load Japanese font
        Font notoSansJP = Resources.FindObjectsOfTypeAll<Font>()
            .FirstOrDefault(f => f.name.Contains("NotoSans"));
        if (notoSansJP != null)
        {
            statusText.font = notoSansJP;
            Debug.Log($"Applied Japanese font to legacy statusText: {notoSansJP.name}");
        }
        else
        {
            Debug.LogWarning("Japanese font not found for legacy UI");
            // システムのデフォルト日本語フォントを試す
            Font[] systemFonts = Resources.FindObjectsOfTypeAll<Font>();
            Debug.Log($"Available system fonts: {string.Join(", ", systemFonts.Select(f => f.name))}");
        }

        RectTransform statusTextRect = statusTextGO.GetComponent<RectTransform>();
        statusTextRect.anchorMin = new Vector2(0.1f, 0.6f);
        statusTextRect.anchorMax = new Vector2(0.9f, 0.8f);
        statusTextRect.offsetMin = Vector2.zero;
        statusTextRect.offsetMax = Vector2.zero;

        // Details Text (UI Text)
        detailsTextGO = new GameObject("DetailsTextLegacy");
        detailsTextGO.transform.SetParent(panelGO.transform);
        detailsText = detailsTextGO.AddComponent<Text>();
        detailsText.text = "";
        detailsText.fontSize = 14;
        detailsText.color = Color.cyan;
        detailsText.alignment = TextAnchor.UpperLeft;

        if (notoSansJP != null)
        {
            detailsText.font = notoSansJP;
        }

        RectTransform detailsTextRect = detailsTextGO.GetComponent<RectTransform>();
        detailsTextRect.anchorMin = new Vector2(0.1f, 0.2f);
        detailsTextRect.anchorMax = new Vector2(0.9f, 0.6f);
        detailsTextRect.offsetMin = Vector2.zero;
        detailsTextRect.offsetMax = Vector2.zero;

        // Dismiss Button
        GameObject buttonGO = new GameObject("DismissButtonLegacy");
        buttonGO.transform.SetParent(panelGO.transform);
        dismissButton = buttonGO.AddComponent<Button>();

        Image buttonImage = buttonGO.AddComponent<Image>();
        buttonImage.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);

        GameObject buttonTextGO = new GameObject("Text");
        buttonTextGO.transform.SetParent(buttonGO.transform);
        Text buttonText = buttonTextGO.AddComponent<Text>();
        buttonText.text = "Dismiss";
        buttonText.fontSize = 16;
        buttonText.color = Color.white;
        buttonText.alignment = TextAnchor.MiddleCenter;

        if (notoSansJP != null)
        {
            buttonText.font = notoSansJP;
        }

        RectTransform buttonTextRect = buttonTextGO.GetComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;

        RectTransform buttonRect = buttonGO.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.4f, 0.1f);
        buttonRect.anchorMax = new Vector2(0.6f, 0.18f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        dismissButton.onClick.AddListener(HideUI);
    }

    public static void ShowStatus(string message)
    {
        Debug.Log("## App Status (Legacy): " + $"[{System.DateTime.Now:HH:mm:ss.fff}] {message}");
        Instance.statusMessages.Add($"[{System.DateTime.Now:HH:mm:ss.fff}] {message}");
        Instance.UpdateDisplay();
    }

    public static void UpdateDeviceStatus(SensorDevice device)
    {
        Debug.Log("## Device Status (Legacy): " + $"[{System.DateTime.Now:HH:mm:ss.fff}] {device.deviceName} - {device.GetDisplayString()}");
        var existingIndex = Instance.deviceStatusList.FindIndex(d => d.deviceName == device.deviceName);
        if (existingIndex >= 0)
        {
            Instance.deviceStatusList[existingIndex] = device;
        }
        else
        {
            Instance.deviceStatusList.Add(device);
        }
        Instance.UpdateDisplay();
    }

    public static void OnFirstFrameProcessed()
    {
        Instance.firstFrameProcessed = true;

        if (Instance.hideOnFirstFrame)
        {
            Instance.shouldAutoHide = true;
            Instance.hideTimer = 0f;
        }
    }

    void UpdateDisplay()
    {
        if (statusText != null && detailsText != null)
        {
            // Main status - show device summary
            int totalDevices = deviceStatusList.Count;
            int activeDevices = 0;
            int gpuDevices = 0;
            int cpuDevices = 0;

            foreach (var device in deviceStatusList)
            {
                if (device.statusType == DeviceStatusType.Active)
                    activeDevices++;
                if (device.processingType == ProcessingType.GPU)
                    gpuDevices++;
                if (device.processingType == ProcessingType.CPU)
                    cpuDevices++;
            }

            if (totalDevices > 0)
            {
                statusText.text = $"デバイス: {activeDevices}/{totalDevices} アクティブ | GPU: {gpuDevices} | CPU: {cpuDevices}";
            }
            else if (statusMessages.Count > 0)
            {
                statusText.text = statusMessages[statusMessages.Count - 1];
            }

            // Device details
            string details = "デバイス状況:\n";
            foreach (var device in deviceStatusList)
            {
                details += $"• {device.deviceName}: {device.GetDisplayString()}\n";
            }

            if (statusMessages.Count > 1)
            {
                details += "\n最近のメッセージ:\n";
                int startIdx = Mathf.Max(0, statusMessages.Count - 5);
                for (int i = startIdx; i < statusMessages.Count - 1; i++)
                {
                    details += $"  {statusMessages[i]}\n";
                }
            }

            detailsText.text = details;
        }
    }

    public void HideUI()
    {
        if (statusCanvas != null)
        {
            statusCanvas.gameObject.SetActive(false);
        }
    }

    public void ShowUI()
    {
        if (statusCanvas != null)
        {
            statusCanvas.gameObject.SetActive(true);
        }
        shouldAutoHide = false;
        hideTimer = 0f;
    }
}
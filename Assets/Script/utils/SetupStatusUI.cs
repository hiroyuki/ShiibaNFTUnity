using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using TMPro;

public class SetupStatusUI : MonoBehaviour
{
    [Header("UI References")]
    public Canvas statusCanvas;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI detailsText;
    public Slider progressBar;
    public Button dismissButton;
    
    // Hold references to prevent garbage collection
    private GameObject canvasGO;
    private GameObject panelGO;
    private GameObject statusTextGO;
    private GameObject detailsTextGO;
    private GameObject progressGO;
    private RectTransform progressFillRect;
    
    [Header("Auto-Hide Settings")]
    public float autoHideDelay = 0.5f; // Increased from 5 to 10 seconds
    public bool hideOnFirstFrame = true;
    
    private static SetupStatusUI instance;
    private List<string> statusMessages = new List<string>();
    private List<SensorDevice> deviceStatusList = new List<SensorDevice>();
    private float hideTimer = 0f;
    private bool shouldAutoHide = false;
    
    public static SetupStatusUI Instance
    {
        get
        {
            if (instance == null)
            {
                // Try to find existing instance
                instance = FindFirstObjectByType<SetupStatusUI>();
                
                // Create one if none exists
                if (instance == null)
                {
                    GameObject go = new GameObject("SetupStatusUI");
                    instance = go.AddComponent<SetupStatusUI>();
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
            
            // Create UI if not already set up
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
        canvasGO = new GameObject("StatusCanvas");
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
        
        // Status Text
        statusTextGO = new GameObject("StatusText");
        statusTextGO.transform.SetParent(panelGO.transform);
        statusText = statusTextGO.AddComponent<TextMeshProUGUI>();
        statusText.text = "Initializing...";
        statusText.fontSize = 24;
        statusText.color = Color.white;
        statusText.alignment = TextAlignmentOptions.Center;

        // Debug: List all available fonts
        Font[] allFonts = UnityEngine.Resources.FindObjectsOfTypeAll<Font>();
        // Debug.Log($"Available fonts: {string.Join(", ", allFonts.Select(f => f.name))}");

        // Try multiple approaches to load Japanese font
        Font notoSansJP = null;
        TMP_FontAsset japaneseFont = null;

        // Method 1: Search by name containing NotoSans
        notoSansJP = allFonts.FirstOrDefault(f => f.name.Contains("NotoSans"));
        if (notoSansJP == null)
        {
            // Method 2: Search by exact name
            notoSansJP = allFonts.FirstOrDefault(f => f.name == "NotoSansJP-VariableFont_wght");
        }
        if (notoSansJP == null)
        {
            // Method 3: Try to load from Resources (if moved to Resources folder)
            notoSansJP = Resources.Load<Font>("NotoSansJP-VariableFont_wght");
        }

        if (notoSansJP != null)
        {
            // Debug.Log($"Found Japanese font: {notoSansJP.name}");
            // Try to create TMP_FontAsset
            try
            {
                japaneseFont = TMP_FontAsset.CreateFontAsset(notoSansJP);
                if (japaneseFont != null)
                {
                    statusText.font = japaneseFont;
                    // Debug.Log("Successfully applied Japanese font to statusText");
                }
                else
                {
                    Debug.LogWarning("Failed to create TMP_FontAsset from NotoSans font");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error creating TMP_FontAsset: {ex.Message}");
            }
        }
        else
        {
            Debug.LogWarning("NotoSans font not found. Available fonts: " + string.Join(", ", allFonts.Select(f => f.name)));

            // Fallback: Try using Arial Unicode MS if available
            Font arialUnicode = allFonts.FirstOrDefault(f => f.name.Contains("Arial") && f.name.Contains("Unicode"));
            if (arialUnicode != null)
            {
                Debug.Log($"Using fallback font: {arialUnicode.name}");
                japaneseFont = TMP_FontAsset.CreateFontAsset(arialUnicode);
                if (japaneseFont != null)
                {
                    statusText.font = japaneseFont;
                }
            }
        }
        
        RectTransform statusTextRect = statusTextGO.GetComponent<RectTransform>();
        statusTextRect.anchorMin = new Vector2(0.1f, 0.6f);
        statusTextRect.anchorMax = new Vector2(0.9f, 0.8f);//右下の値（offsetMaxがゼロの場合）
        statusTextRect.offsetMin = Vector2.zero;
        statusTextRect.offsetMax = Vector2.zero;
        
        // Details Text
        detailsTextGO = new GameObject("DetailsText");
        detailsTextGO.transform.SetParent(panelGO.transform);
        detailsText = detailsTextGO.AddComponent<TextMeshProUGUI>();
        detailsText.text = "";
        detailsText.fontSize = 14;
        detailsText.color = Color.cyan;
        detailsText.alignment = TextAlignmentOptions.TopLeft;

        // Apply same Japanese font to details text
        if (japaneseFont != null)
        {
            detailsText.font = japaneseFont;
        }
        
        RectTransform detailsTextRect = detailsTextGO.GetComponent<RectTransform>();
        detailsTextRect.anchorMin = new Vector2(0.1f, 0.2f);
        detailsTextRect.anchorMax = new Vector2(0.9f, 0.6f);
        detailsTextRect.offsetMin = Vector2.zero;
        detailsTextRect.offsetMax = Vector2.zero;
        
        // Progress bar removed - was not displaying properly
        
        // Dismiss Button
        GameObject buttonGO = new GameObject("DismissButton");
        buttonGO.transform.SetParent(panelGO.transform);
        dismissButton = buttonGO.AddComponent<Button>();
        
        Image buttonImage = buttonGO.AddComponent<Image>();
        buttonImage.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
        
        GameObject buttonTextGO = new GameObject("Text");
        buttonTextGO.transform.SetParent(buttonGO.transform);
        TextMeshProUGUI buttonText = buttonTextGO.AddComponent<TextMeshProUGUI>();
        buttonText.text = "Dismiss";
        buttonText.fontSize = 16;
        buttonText.color = Color.white;
        buttonText.alignment = TextAlignmentOptions.Center;
        
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
        Debug.Log("## App Status: " + $"[{System.DateTime.Now:HH:mm:ss.fff}] {message}");
        Instance.statusMessages.Add($"[{System.DateTime.Now:HH:mm:ss.fff}] {message}");
        Instance.UpdateDisplay();
    }
    
    public static void UpdateDeviceStatus(SensorDevice device)
    {
        Debug.Log("## Device Status: " + $"[{System.DateTime.Now:HH:mm:ss.fff}] {device.deviceName} - {device.GetDisplayString()}");
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
    
    public static void SetProgress(float progress)
    {
        // Progress bar removed - method kept for compatibility
    }
    
    public static void OnFirstFrameProcessed()
    {   
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
                statusText.text = $"Devices: {activeDevices}/{totalDevices} active | GPU: {gpuDevices} | CPU: {cpuDevices}";
            }
            else if (statusMessages.Count > 0)
            {
                statusText.text = statusMessages[statusMessages.Count - 1];
            }
            
            // Device details
            string details = "Device Status:\n";
            foreach (var device in deviceStatusList)
            {
                details += $"• {device.deviceName}: {device.GetDisplayString()}\n";
            }
            
            if (statusMessages.Count > 1)
            {
                details += "\nRecent Messages:\n";
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
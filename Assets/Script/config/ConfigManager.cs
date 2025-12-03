using UnityEngine;

/// <summary>
/// Central configuration manager for the application
/// Holds reference to DatasetConfig and makes it discoverable across the entire system
/// Attach this to the World GameObject (parent of all displayable GameObjects)
/// </summary>
public class ConfigManager : MonoBehaviour
{
    [SerializeField] private DatasetConfig datasetConfig;
    
    private static ConfigManager instance;

    private void OnEnable()
    {
        instance = this;
        Debug.Log("[ConfigManager] ConfigManager enabled and ready");
    }

    private void OnDisable()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    /// <summary>
    /// Get the current DatasetConfig from the ConfigManager
    /// </summary>
    public static DatasetConfig GetDatasetConfig()
    {
        if (instance != null && instance.datasetConfig != null)
        {
            return instance.datasetConfig;
        }

        Debug.LogWarning("[ConfigManager] No DatasetConfig found in ConfigManager instance");
        return null;
    }

    /// <summary>
    /// Set the DatasetConfig at runtime
    /// Called from PointCloudPlayableAsset or other initialization code
    /// </summary>
    public static void SetDatasetConfig(DatasetConfig config)
    {
        if (instance != null)
        {
            instance.datasetConfig = config;
            if (config != null)
            {
                Debug.Log($"[ConfigManager] DatasetConfig set to: {config.DatasetName}");
            }
        }
        else
        {
            Debug.LogWarning("[ConfigManager] Cannot set DatasetConfig - ConfigManager instance not found");
        }
    }
}

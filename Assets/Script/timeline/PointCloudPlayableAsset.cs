using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class PointCloudPlayableAsset : PlayableAsset, ITimelineClipAsset
{
    [SerializeField] private float frameRate = 30f;

    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject go)
    {
        var playable = ScriptPlayable<PointCloudPlayableBehaviour>.Create(graph);
        var behaviour = playable.GetBehaviour();

        // Use frameRate from local setting
        behaviour.frameRate = frameRate;

        // Find the manager in the scene
        behaviour.pointCloudManager = Object.FindFirstObjectByType<MultiCameraPointCloudManager>();

        if (behaviour.pointCloudManager == null)
        {
            Debug.LogError("PointCloudPlayableAsset: MultiCameraPointCloudManager not found in scene!");
            return playable;
        }

        // Get DatasetConfig from ConfigManager (centralized config storage)
        DatasetConfig configToUse = DatasetConfig.GetInstance();

        if (configToUse == null)
        {
            Debug.LogWarning("PointCloudPlayableAsset: DatasetConfig not found in ConfigManager. Ensure ConfigManager is in scene with DatasetConfig assigned.");
        }
        else
        {
            Debug.Log($"PointCloudPlayableAsset: Using DatasetConfig from ConfigManager: {configToUse.DatasetName}");
            // Pass config to manager so it can initialize
            behaviour.pointCloudManager.SetDatasetConfig(configToUse);
        }

        return playable;
    }
}

using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class PointCloudPlayableAsset : PlayableAsset, ITimelineClipAsset
{
    [SerializeField] private float frameRate = 30f;
    [SerializeField] private DatasetConfig datasetConfig;
    [Tooltip("DatasetConfig to use for this point cloud timeline. If set, overrides automatic loading.")]

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

        // Use DatasetConfig from inspector field, or try to find one in the scene
        DatasetConfig configToUse = datasetConfig;

        if (configToUse == null)
        {
            // Try finding a DatasetConfig in the scene as fallback
            configToUse = Object.FindFirstObjectByType<DatasetConfig>();
            if (configToUse != null)
            {
                Debug.Log("PointCloudPlayableAsset: Using DatasetConfig found in scene");
            }
        }

        if (configToUse != null)
        {
            behaviour.pointCloudManager.SetDatasetConfig(configToUse);
            Debug.Log($"PointCloudPlayableAsset: Set DatasetConfig: {configToUse.DatasetName}");
        }
        else
        {
            Debug.LogError("PointCloudPlayableAsset: DatasetConfig not assigned in inspector and none found in scene!");
        }

        return playable;
    }
}

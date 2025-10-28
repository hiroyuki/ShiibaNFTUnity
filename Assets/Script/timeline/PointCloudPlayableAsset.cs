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
            Debug.LogWarning("MultiCameraPointCloudManager not found in scene!");
            return playable;
        }

        // Load DatasetConfig from Assets/Data/DatasetConfig.asset
        DatasetConfig datasetConfig = Resources.Load<DatasetConfig>("Data/DatasetConfig");
        if (datasetConfig == null)
        {
            // Try direct path if Resources.Load doesn't work
            #if UNITY_EDITOR
            datasetConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<DatasetConfig>("Assets/Data/DatasetConfig.asset");
            #endif
        }

        if (datasetConfig != null)
        {
            behaviour.pointCloudManager.SetDatasetConfig(datasetConfig);
            Debug.Log($"PointCloudPlayableAsset: Loaded DatasetConfig: {datasetConfig.DatasetName}");
        }
        else
        {
            Debug.LogWarning("PointCloudPlayableAsset: Could not load DatasetConfig from Assets/Data/DatasetConfig.asset");
        }

        return playable;
    }
}

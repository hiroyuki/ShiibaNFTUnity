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

        // Get DatasetConfig from MultiCameraPointCloudManager
        // Note: At this point, the manager's Start() may not have run yet, so currentDatasetConfig could be null
        DatasetConfig configToUse = behaviour.pointCloudManager.GetDatasetConfig();

        // If not found in manager's runtime config, the manager should load it from its serialized field in Start()
        // The manager will handle finding the DatasetConfig via its own fallback logic
        if (configToUse == null)
        {
            Debug.Log("PointCloudPlayableAsset: DatasetConfig not yet initialized. Manager will load it in Start().");
        }
        else
        {
            Debug.Log($"PointCloudPlayableAsset: Using DatasetConfig from MultiCameraPointCloudManager: {configToUse.DatasetName}");
        }

        return playable;
    }
}

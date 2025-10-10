using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class PointCloudPlayableAsset : PlayableAsset, ITimelineClipAsset
{
    public float frameRate = 30f;

    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject go)
    {
        var playable = ScriptPlayable<PointCloudPlayableBehaviour>.Create(graph);
        var behaviour = playable.GetBehaviour();

        behaviour.frameRate = frameRate;

        // Find the manager in the scene
        behaviour.pointCloudManager = Object.FindFirstObjectByType<MultiCameraPointCloudManager>();

        if (behaviour.pointCloudManager == null)
        {
            Debug.LogWarning("MultiCameraPointCloudManager not found in scene!");
        }

        return playable;
    }
}

using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class PointCloudPlayableAsset : PlayableAsset
{
    [SerializeField] public float frameRate = 30f;
    [SerializeField] public ExposedReference<MultiCameraPointCloudManager> pointCloudManager;
    
    public override Playable CreatePlayable(PlayableGraph graph, GameObject go)
    {
        var playable = ScriptPlayable<PointCloudPlayableBehaviour>.Create(graph);
        var behaviour = playable.GetBehaviour();
        
        behaviour.frameRate = frameRate;
        behaviour.pointCloudManager = pointCloudManager.Resolve(graph.GetResolver());
        
        return playable;
    }
}

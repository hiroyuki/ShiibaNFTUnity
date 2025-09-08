using UnityEngine;
using UnityEngine.Playables;

public class PointCloudPlayableBehaviour : PlayableBehaviour
{
    public float frameRate = 30f;
    public MultiCameraPointCloudManager pointCloudManager;
    
    private double lastFrameTime = -1;
    private int currentFrame = -1;
    
    public override void OnGraphStart(Playable playable)
    {
        if (pointCloudManager != null)
        {
            pointCloudManager.ResetToFirstFrame();
        }
    }

    public override void OnGraphStop(Playable playable)
    {
        
    }

    public override void OnBehaviourPlay(Playable playable, FrameData info)
    {
        
    }

    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        
    }

    public override void PrepareFrame(Playable playable, FrameData info)
    {
        Debug.Log($"PrepareFrame called - pointCloudManager null: {pointCloudManager == null}");
        
        if (pointCloudManager == null) 
        {
            Debug.LogWarning("pointCloudManager is null in PrepareFrame");
            return;
        }
        
        double currentTime = playable.GetTime();
        int targetFrame = Mathf.FloorToInt((float)(currentTime * frameRate));
        
        Debug.Log($"Timeline time: {currentTime}, target frame: {targetFrame}, current: {currentFrame}");
        
        if (targetFrame != currentFrame)
        {
            Debug.Log($"Seeking to frame {targetFrame}");
            pointCloudManager.SeekToFrame(targetFrame);
            currentFrame = targetFrame;
        }
    }
}

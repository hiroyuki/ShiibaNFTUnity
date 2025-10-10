using UnityEngine;
using UnityEngine.Playables;

public class PointCloudPlayableBehaviour : PlayableBehaviour
{
    public float frameRate = 30f;
    public MultiCameraPointCloudManager pointCloudManager;
    
    private int currentFrame = -1;
    
    public override void OnGraphStart(Playable playable)
    {
        // Don't auto-reset to first frame - let user control timeline position
        // if (pointCloudManager != null)
        // {
        //     pointCloudManager.ResetToFirstFrame();
        // }
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
        if (pointCloudManager == null) return;
        
        double currentTime = playable.GetTime();
        int targetFrame = Mathf.FloorToInt((float)(currentTime * frameRate));
        
        if (targetFrame != currentFrame)
        {
            pointCloudManager.SeekToFrame(targetFrame);
            currentFrame = targetFrame;
        }
    }
}

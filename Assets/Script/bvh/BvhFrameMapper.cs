using UnityEngine;

/// <summary>
/// Pure utility class for mapping Timeline time to BVH frame indices with keyframe interpolation
/// Completely decoupled from Timeline - can be used by any component to calculate frame numbers
///
/// Features:
/// - Maps Timeline time to BVH frame index using keyframe interpolation if available
/// - Falls back to linear mapping if no keyframes are provided
/// - Supports frame offset for synchronization
/// - Stateless and reusable by multiple components
/// </summary>
public class BvhFrameMapper
{
    /// <summary>
    /// Get the target BVH frame for the given timeline time, including offset and clamping
    /// Uses keyframe-based mapping if drift correction keyframes are available, otherwise uses linear mapping
    /// </summary>
    /// <param name="timelineTime">Time in seconds on the Timeline</param>
    /// <param name="bvhData">BVH data containing frame count and frame rate</param>
    /// <param name="driftCorrectionData">Optional drift correction data with keyframes (can be null)</param>
    /// <param name="frameOffset">Frame offset for synchronization with other data (e.g., point cloud frames)</param>
    /// <returns>Clamped BVH frame index [0, FrameCount-1]</returns>
    public int GetTargetFrameForTime(float timelineTime, BvhData bvhData, BvhDriftCorrectionData driftCorrectionData, int frameOffset)
    {
        if (bvhData == null)
            return 0;

        int targetFrame = CalculateTargetFrame(timelineTime, bvhData.FrameRate, driftCorrectionData);
        targetFrame += frameOffset;
        return Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1);
    }

    /// <summary>
    /// Calculate target frame using keyframe-based mapping if available, otherwise use linear mapping
    ///
    /// When drift correction keyframes are available, uses them to define Timeline-to-BVH-frame mapping:
    /// - Finds keyframes before and after current Timeline time
    /// - Interpolates bvhFrameNumber between keyframes
    /// - This allows speed adjustment by modifying bvhFrameNumber values in keyframes
    ///
    /// Falls back to linear mapping if no keyframes are available.
    /// </summary>
    private int CalculateTargetFrame(float currentTime, float bvhFrameRate, BvhDriftCorrectionData driftCorrectionData)
    {
        if (driftCorrectionData == null || driftCorrectionData.GetKeyframeCount() == 0)
        {
            // Fall back to linear mapping when no keyframes available
            return Mathf.FloorToInt((float)(currentTime * bvhFrameRate));
        }

        FindSurroundingKeyframes(currentTime, driftCorrectionData, out BvhKeyframe prevKeyframe, out BvhKeyframe nextKeyframe);
        return InterpolateFrameNumber(currentTime, prevKeyframe, nextKeyframe, bvhFrameRate);
    }

    /// <summary>
    /// Find keyframes that surround the given time
    /// </summary>
    private void FindSurroundingKeyframes(float currentTime, BvhDriftCorrectionData driftCorrectionData, out BvhKeyframe prevKeyframe, out BvhKeyframe nextKeyframe)
    {
        prevKeyframe = null;
        nextKeyframe = null;

        var keyframes = driftCorrectionData.GetAllKeyframes();
        foreach (var kf in keyframes)
        {
            if (kf.timelineTime <= currentTime)
                prevKeyframe = kf;
            else if (nextKeyframe == null)
                nextKeyframe = kf;
        }
    }

    /// <summary>
    /// Interpolate BVH frame number based on surrounding keyframes
    /// </summary>
    private int InterpolateFrameNumber(float currentTime, BvhKeyframe prevKeyframe, BvhKeyframe nextKeyframe, float bvhFrameRate)
    {
        // Case 1: Between two keyframes - interpolate frame number
        if (prevKeyframe != null && nextKeyframe != null)
        {
            double timeDelta = nextKeyframe.timelineTime - prevKeyframe.timelineTime;
            if (timeDelta > 0)
            {
                float frameDelta = nextKeyframe.bvhFrameNumber - prevKeyframe.bvhFrameNumber;
                double t = (currentTime - prevKeyframe.timelineTime) / timeDelta;
                t = Mathf.Clamp01((float)t);
                return Mathf.FloorToInt((float)(prevKeyframe.bvhFrameNumber + (frameDelta * t)));
            }
            return prevKeyframe.bvhFrameNumber;
        }

        // Case 2: Before first keyframe - interpolate from (0s, frame 0) to first keyframe
        if (prevKeyframe == null && nextKeyframe != null)
        {
            double timeDelta = nextKeyframe.timelineTime;
            if (timeDelta > 0)
            {
                float frameDelta = nextKeyframe.bvhFrameNumber;
                float t = currentTime / (float)timeDelta;
                t = Mathf.Clamp01(t);
                return Mathf.FloorToInt(frameDelta * t);
            }
            return 0;
        }

        // Case 3: After last keyframe - extrapolate using BVH file's native frame rate
        if (prevKeyframe != null && nextKeyframe == null)
        {
            double timeSincePrevKeyframe = currentTime - prevKeyframe.timelineTime;
            double additionalFrames = timeSincePrevKeyframe * bvhFrameRate;
            return prevKeyframe.bvhFrameNumber + Mathf.FloorToInt((float)additionalFrames);
        }

        // Case 4: No surrounding keyframes (shouldn't happen if keyframes exist)
        return Mathf.FloorToInt((float)(currentTime * bvhFrameRate));
    }
}

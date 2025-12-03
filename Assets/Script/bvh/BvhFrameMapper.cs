using UnityEngine;

/// <summary>
/// Pure utility class for mapping Timeline time to BVH frame indices with keyframe interpolation.
///
/// This class provides Timeline-agnostic frame mapping that can be used by any component needing
/// to convert Timeline playback time into BVH frame indices. It handles four interpolation cases
/// for robustness and flexibility.
///
/// Timeline-Independent Design:
/// - Completely decoupled from Timeline system (no dependencies on PlayableBehaviour or PlayableDirector)
/// - All parameters passed explicitly (stateless)
/// - Can be instantiated fresh for each call or cached and reused
/// - Works in offline processing, Scene Flow calculations, or other contexts
///
/// Features:
/// - Maps Timeline time → BVH frame index using keyframe interpolation when available
/// - Falls back to linear time-based mapping if no keyframes
/// - Handles four interpolation cases covering all time ranges
/// - Supports frame offset for synchronization with multi-camera data
/// - Clamping prevents out-of-bounds frame access
///
/// Interpolation Cases:
/// 1. Between keyframes: Interpolates frame number linearly between surrounding keyframes
///    Enables timeline speed adjustment by setting bvhFrameNumber in keyframes
/// 2. Before first keyframe: Interpolates from implicit (time=0s, frame=0) to first keyframe
/// 3. After last keyframe: Extrapolates using BVH file's native frame rate
/// 4. No keyframes: Falls back to linear time-based mapping (timelineTime * frameRate)
///
/// Example Usage:
/// ```csharp
/// var mapper = new BvhFrameMapper();
/// int frameIndex = mapper.GetTargetFrameForTime(2.5f, bvhData, driftData, frameOffset: 0);
/// ```
/// </summary>
public class BvhFrameMapper
{
    /// <summary>
    /// Get the target BVH frame for the given timeline time, handling both keyframe-based and linear mapping.
    ///
    /// This method:
    /// 1. Calculates target frame using keyframe interpolation (if available) or linear mapping
    /// 2. Applies optional frame offset for synchronization
    /// 3. Clamps result to valid range [0, FrameCount-1]
    ///
    /// The mapping is flexible and continues to work if drift correction data changes:
    /// - If driftCorrectionData is null or has no keyframes, uses linear mapping
    /// - If driftCorrectionData is provided with keyframes, uses keyframe interpolation
    /// - If keyframes are added/removed later, updates behavior automatically
    /// </summary>
    /// <param name="timelineTime">Time in seconds from the Timeline playhead</param>
    /// <param name="bvhData">BVH data container with frame count and frame rate information</param>
    /// <param name="driftCorrectionData">Optional drift correction keyframes (can be null for simple linear mapping)</param>
    /// <param name="frameOffset">Frame offset for synchronization with other systems (default: 0)</param>
    /// <returns>Clamped BVH frame index guaranteed to be in range [0, FrameCount-1]</returns>
    public int GetTargetFrameForTime(float timelineTime, BvhData bvhData, BvhDriftCorrectionData driftCorrectionData, int frameOffset)
    {
        if (bvhData == null)
            return 0;

        int targetFrame = CalculateTargetFrame(timelineTime, bvhData.FrameRate, driftCorrectionData);
        targetFrame += frameOffset;
        return Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1);
    }

    /// <summary>
    /// Calculate target frame using keyframe-based mapping if available, otherwise use linear mapping.
    ///
    /// Decision logic:
    /// - If drift correction keyframes exist: Uses keyframe interpolation (4 cases)
    /// - If no keyframes: Uses linear mapping (Case 4 fallback: frame = timelineTime * frameRate)
    ///
    /// When drift correction keyframes are available, they define a Timeline-to-BVH-frame mapping:
    /// - Each keyframe specifies a (timelineTime, bvhFrameNumber) pair
    /// - Frame numbers are interpolated between keyframes
    /// - This enables timeline speed adjustment and frame skipping via keyframe editing
    ///
    /// Linear fallback is used when:
    /// - No drift correction data is provided
    /// - Drift correction data has no keyframes
    /// </summary>
    private int CalculateTargetFrame(float currentTime, float bvhFrameRate, BvhDriftCorrectionData driftCorrectionData)
    {
        if (driftCorrectionData == null || driftCorrectionData.GetKeyframeCount() == 0)
        {
            // Case 4 (fallback): Linear time-based mapping
            // Used when no keyframes available
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
    /// Interpolate BVH frame number based on surrounding keyframes.
    ///
    /// Handles all four interpolation cases:
    /// - Case 1: Between two keyframes → Linear interpolation of frame number
    /// - Case 2: Before first keyframe → Interpolate from implicit (0s, frame 0)
    /// - Case 3: After last keyframe → Extrapolate using BVH native frame rate
    /// - Case 4: No keyframes → Linear time-based mapping (handled in CalculateTargetFrame)
    ///
    /// This design allows:
    /// - Speed adjustment: Set bvhFrameNumber in keyframes to skip or slow down frames
    /// - Continuous playback: Smooth transitions before first and after last keyframe
    /// - Fallback robustness: Always produces valid frame indices even with missing keyframes
    /// </summary>
    private int InterpolateFrameNumber(float currentTime, BvhKeyframe prevKeyframe, BvhKeyframe nextKeyframe, float bvhFrameRate)
    {
        // Case 1: Between two keyframes - interpolate frame number
        // Allows timeline speed adjustment via keyframe frame numbers
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

        // Case 2: Before first keyframe - interpolate from implicit (0s, frame 0) to first keyframe
        // Handles timeline time before any drift correction keyframes
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
        // Continues playback at regular speed after last keyframe
        if (prevKeyframe != null && nextKeyframe == null)
        {
            double timeSincePrevKeyframe = currentTime - prevKeyframe.timelineTime;
            double additionalFrames = timeSincePrevKeyframe * bvhFrameRate;
            return prevKeyframe.bvhFrameNumber + Mathf.FloorToInt((float)additionalFrames);
        }

        // Case 4: No surrounding keyframes (shouldn't happen if keyframes exist)
        // Fallback to linear mapping as safety net
        return Mathf.FloorToInt((float)(currentTime * bvhFrameRate));
    }
}

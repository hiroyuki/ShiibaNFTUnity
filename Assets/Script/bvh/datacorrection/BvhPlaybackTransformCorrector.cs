using UnityEngine;

/// <summary>
/// Calculates playback-corrected root transforms (position and rotation).
///
/// This utility class applies position and rotation corrections from BvhPlaybackCorrectionKeyframes
/// to the BVH character's root transform. It enables fine-tuning of character position and orientation
/// without modifying the underlying BVH animation data.
///
/// Key Features:
/// - Calculates corrected root position using keyframe interpolation
/// - Calculates corrected root rotation using keyframe interpolation
/// - Combines baseline offsets with keyframe-based corrections
/// - Stateless and reusable by multiple components
/// - Timeline-independent (works offline or in any context)
///
/// How It Works:
/// Each keyframe in BvhPlaybackCorrectionKeyframes specifies correction values at specific timeline times.
/// This class interpolates between keyframes to provide smooth corrections throughout playback.
/// </summary>
public class BvhPlaybackTransformCorrector
{
    /// <summary>
    /// Calculate the playback-corrected root position for the given timeline time.
    ///
    /// Combines baseline position offset with keyframe-based corrections through interpolation.
    /// This allows adjusting character position without modifying the BVH animation data.
    /// </summary>
    /// <param name="timelineTime">Time in seconds on the Timeline</param>
    /// <param name="correctionKeyframes">Playback correction keyframes containing position corrections (can be null)</param>
    /// <param name="positionOffset">Baseline position offset from DatasetConfig</param>
    /// <returns>Corrected local position for BVH character root</returns>
    public static Vector3 GetCorrectedRootPosition(double timelineTime, BvhPlaybackCorrectionKeyframes correctionKeyframes, Vector3 positionOffset)
    {
        if (correctionKeyframes == null || !correctionKeyframes.IsEnabled)
            return positionOffset;

        // Get target anchor position from keyframe interpolation
        Vector3 targetAnchorPositionRelative = correctionKeyframes.GetAnchorPositionAtTime(timelineTime);

        // Apply position correction: baseline position (positionOffset) + keyframe-based correction
        // This preserves the initial character position while applying playback corrections
        return positionOffset + targetAnchorPositionRelative;
    }

    /// <summary>
    /// Calculate the playback-corrected root rotation for the given timeline time.
    ///
    /// Combines baseline rotation offset with keyframe-based corrections through interpolation.
    /// This allows adjusting character orientation without modifying the BVH animation data.
    /// </summary>
    /// <param name="timelineTime">Time in seconds on the Timeline</param>
    /// <param name="correctionKeyframes">Playback correction keyframes containing rotation corrections (can be null)</param>
    /// <param name="rotationOffset">Baseline rotation offset from DatasetConfig (euler angles)</param>
    /// <returns>Corrected local rotation for BVH character root</returns>
    public static Quaternion GetCorrectedRootRotation(double timelineTime, BvhPlaybackCorrectionKeyframes correctionKeyframes, Vector3 rotationOffset)
    {
        if (correctionKeyframes == null || !correctionKeyframes.IsEnabled)
            return Quaternion.Euler(rotationOffset);

        // Get target anchor rotation from keyframe interpolation
        Vector3 targetAnchorRotationRelative = correctionKeyframes.GetAnchorRotationAtTime(timelineTime);

        // Apply rotation correction: baseline rotation + keyframe-based rotation correction
        // Convert both euler angles to quaternions and combine them
        Quaternion baseRotation = Quaternion.Euler(rotationOffset);
        Quaternion rotationCorrection = Quaternion.Euler(targetAnchorRotationRelative);
        return baseRotation * rotationCorrection;
    }
}

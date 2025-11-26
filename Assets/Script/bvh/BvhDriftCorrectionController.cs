using UnityEngine;

/// <summary>
/// Pure utility class for calculating drift-corrected positions and rotations
/// Completely decoupled from Timeline - can be used by any component to calculate corrections
///
/// Features:
/// - Calculates drift-corrected root position for any timeline time
/// - Calculates drift-corrected root rotation for any timeline time
/// - Uses keyframe interpolation from BvhDriftCorrectionData
/// - Stateless and reusable by multiple components
/// - Works offline without Timeline running
/// </summary>
public class BvhDriftCorrectionController
{
    /// <summary>
    /// Calculate the drift-corrected position for BVH_Character root at the given timeline time
    /// </summary>
    /// <param name="timelineTime">Time in seconds on the Timeline</param>
    /// <param name="driftCorrectionData">Drift correction data containing keyframe interpolation (can be null)</param>
    /// <param name="positionOffset">Baseline position offset from DatasetConfig</param>
    /// <returns>Drift-corrected local position for BVH_Character root</returns>
    public Vector3 GetCorrectedRootPosition(float timelineTime, BvhDriftCorrectionData driftCorrectionData, Vector3 positionOffset)
    {
        if (driftCorrectionData == null || !driftCorrectionData.IsEnabled)
            return positionOffset;

        // Get target anchor position from keyframe interpolation
        Vector3 targetAnchorPositionRelative = driftCorrectionData.GetAnchorPositionAtTime(timelineTime);

        // Apply position correction: baseline position (positionOffset) + anchor correction
        // This preserves the initial BVH_Character position while applying drift correction
        return positionOffset + targetAnchorPositionRelative;
    }

    /// <summary>
    /// Calculate the drift-corrected rotation for BVH_Character root at the given timeline time
    /// </summary>
    /// <param name="timelineTime">Time in seconds on the Timeline</param>
    /// <param name="driftCorrectionData">Drift correction data containing keyframe interpolation (can be null)</param>
    /// <param name="rotationOffset">Baseline rotation offset from DatasetConfig (euler angles)</param>
    /// <returns>Drift-corrected local rotation for BVH_Character root</returns>
    public Quaternion GetCorrectedRootRotation(float timelineTime, BvhDriftCorrectionData driftCorrectionData, Vector3 rotationOffset)
    {
        if (driftCorrectionData == null || !driftCorrectionData.IsEnabled)
            return Quaternion.Euler(rotationOffset);

        // Get target anchor rotation from keyframe interpolation
        Vector3 targetAnchorRotationRelative = driftCorrectionData.GetAnchorRotationAtTime(timelineTime);

        // Apply rotation correction: baseline rotation + anchor rotation correction
        // Convert both euler angles to quaternions and combine them
        Quaternion baseRotation = Quaternion.Euler(rotationOffset);
        Quaternion rotationCorrection = Quaternion.Euler(targetAnchorRotationRelative);
        return baseRotation * rotationCorrection;
    }
}

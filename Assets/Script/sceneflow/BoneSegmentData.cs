using UnityEngine;

/// <summary>
/// Unified data class for bone segment visualization and motion vector calculation
/// Represents a single point on a bone (segment) with position and motion information
/// Covers both individual segments (e.g., bone positions) and joint positions
/// </summary>
[System.Serializable]
public class SegmentedBoneMotionData
{
    /// <summary>Index of the parent bone</summary>
    public int boneIndex;

    /// <summary>Position of this segment along the bone (0 to segmentsPerBone-1)</summary>
    public int segmentIndex;

    /// <summary>World space position of this segment in current frame</summary>
    public Vector3 position;

    /// <summary>World space position of this segment in previous frame (optional)</summary>
    public Vector3 previousPosition;

    /// <summary>Interpolation parameter along the bone (0.0 = parent joint, 1.0 = child joint)</summary>
    public float interpolationT;

    /// <summary>Name of the parent bone for debugging</summary>
    public string boneName;

    /// <summary>Motion vector (currentPosition - previousPosition). Only set when motion calculation is performed</summary>
    public Vector3 motionVector;

    /// <summary>Magnitude of motion. Only set when motion calculation is performed</summary>
    public float motionMagnitude;

    /// <summary>
    /// Constructor for SegmentedBoneMotionData - position only (visualization)
    /// </summary>
    public SegmentedBoneMotionData(int boneIdx, int segIdx, Vector3 pos, float t, string name)
    {
        boneIndex = boneIdx;
        segmentIndex = segIdx;
        position = pos;
        previousPosition = Vector3.zero;
        interpolationT = t;
        boneName = name;
        motionVector = Vector3.zero;
        motionMagnitude = 0f;
    }

    /// <summary>
    /// Constructor for SegmentedBoneMotionData - with motion information (analysis)
    /// </summary>
    public SegmentedBoneMotionData(int boneIdx, int segIdx, Vector3 current, Vector3 previous, float t, string name)
    {
        boneIndex = boneIdx;
        segmentIndex = segIdx;
        position = current;
        previousPosition = previous;
        interpolationT = t;
        boneName = name;
        motionVector =  previous - current;
        motionMagnitude = motionVector.magnitude;
    }
}

/// <summary>
/// GPU-compatible bone segment data structure for motion vector assignment
/// Matches memory layout for ComputeBuffer upload (StructLayout.Sequential)
/// Used for passing bone segment data to compute shaders or offline processing
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct BoneSegmentGPUData
{
    public Vector3 currentPosition;
    public Vector3 previousPosition;
    public Vector3 motionVector;
    public int boneIndex;
    public int segmentIndex;
    public float interpolationT;
    public float motionMagnitude;
}

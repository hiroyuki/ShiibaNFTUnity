using UnityEngine;

/// <summary>
/// Stores joint motion vector information for visualization
/// Contains current and previous frame positions, calculated motion vectors
/// </summary>
[System.Serializable]
public class JointMotionData
{
    /// <summary>Joint name for identification</summary>
    public string jointName;

    /// <summary>Joint transform in current frame</summary>
    public Transform jointTransform;

    /// <summary>World position of joint in current frame</summary>
    public Vector3 currentPosition;

    /// <summary>World position of joint in previous frame</summary>
    public Vector3 previousPosition;

    /// <summary>Motion vector (currentPosition - previousPosition)</summary>
    public Vector3 motionVector;

    /// <summary>Magnitude of motion</summary>
    public float motionMagnitude;

    /// <summary>
    /// Constructor for JointMotionData
    /// </summary>
    public JointMotionData(string name, Transform transform, Vector3 current, Vector3 previous)
    {
        jointName = name;
        jointTransform = transform;
        currentPosition = current;
        previousPosition = previous;
        motionVector = current - previous;
        motionMagnitude = motionVector.magnitude;
    }
}

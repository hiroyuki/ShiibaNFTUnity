using UnityEngine;

/// <summary>
/// Utility class for reading and parsing BVH channel data
/// Centralizes channel parsing logic to avoid duplication across BvhData and BvhPlayableBehaviour
/// </summary>
public static class BvhChannelReader
{
    /// <summary>
    /// Read channel data from frame data and extract position and rotation values
    /// </summary>
    /// <param name="channels">List of channel names (e.g., "Xposition", "Zrotation")</param>
    /// <param name="frameData">Frame data array containing channel values</param>
    /// <param name="channelIndex">Current index in frameData (passed by reference, will be incremented)</param>
    /// <param name="position">Output position vector (modified by XPOSITION, YPOSITION, ZPOSITION channels)</param>
    /// <param name="rotation">Output rotation vector in euler angles (modified by XROTATION, YROTATION, ZROTATION)</param>
    public static void ReadChannelData(
        System.Collections.Generic.List<string> channels,
        float[] frameData,
        ref int channelIndex,
        ref Vector3 position,
        ref Vector3 rotation)
    {
        foreach (string channel in channels)
        {
            if (channelIndex >= frameData.Length)
                break;

            float value = frameData[channelIndex];
            channelIndex++;

            switch (channel.ToUpper())
            {
                case "XPOSITION":
                    position.x = value;
                    break;
                case "YPOSITION":
                    position.y = value;
                    break;
                case "ZPOSITION":
                    position.z = value;
                    break;
                case "XROTATION":
                    rotation.x = value;
                    break;
                case "YROTATION":
                    rotation.y = value;
                    break;
                case "ZROTATION":
                    rotation.z = value;
                    break;
            }
        }
    }

    /// <summary>
    /// Convert euler angles to quaternion in ZXY order (mocopi default)
    /// </summary>
    /// <param name="eulerAngles">Euler angles (X, Y, Z)</param>
    /// <returns>Quaternion representing the rotation in ZXY order</returns>
    public static Quaternion GetRotationQuaternion(Vector3 eulerAngles)
    {
        Quaternion qZ = Quaternion.AngleAxis(eulerAngles.z, Vector3.forward);
        Quaternion qX = Quaternion.AngleAxis(eulerAngles.x, Vector3.right);
        Quaternion qY = Quaternion.AngleAxis(eulerAngles.y, Vector3.up);
        return qZ * qX * qY;
    }

    /// <summary>
    /// Check if channel data contains position channels (XPOSITION, YPOSITION, ZPOSITION)
    /// </summary>
    public static bool HasPositionChannels(System.Collections.Generic.List<string> channels)
    {
        foreach (string channel in channels)
        {
            if (channel.ToUpper().Contains("POSITION"))
                return true;
        }
        return false;
    }
}

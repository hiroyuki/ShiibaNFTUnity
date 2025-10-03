using UnityEngine;
using System.Runtime.InteropServices;

/// <summary>
/// Camera metadata structure that matches ComputeShader layout.
/// IMPORTANT: Field order and types must exactly match MultiCamDepthToPointCloud.compute
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CameraMetadata
{
    // Transform matrices
    public Matrix4x4 d2cRotation;           // float4x4 rotationMatrix
    public Vector3 d2cTranslation;          // float3 translation (+ 4 bytes padding)
    public Matrix4x4 depthViewerTransform;  // float4x4 depthViewerTransform

    // Camera intrinsics
    public float fx_d, fy_d, cx_d, cy_d;    // Depth camera
    public float fx_c, fy_c, cx_c, cy_c;    // Color camera

    // Color distortion parameters (matches ComputeShader layout)
    // Note: Depth distortion is NOT in ComputeShader - it's pre-computed in LUT
    public float k1_c, k2_c, p1_c, p2_c;    // float4 colorDistortion (k1, k2, p1, p2)
    public float k3_c, k4_c, k5_c, k6_c;    // float4 colorDistortion2 (k3, k4, k5, k6)

    // Image dimensions
    public uint depthWidth;
    public uint depthHeight;
    public uint colorWidth;
    public uint colorHeight;

    // Processing parameters
    public float depthScaleFactor;
    public float depthBias;
    public int useOpenCVLUT;                // bool represented as int

    // Bounding volume parameters
    public int hasBoundingVolume;           // bool represented as int
    public int showAllPoints;               // bool represented as int
    public Matrix4x4 boundingVolumeInverseTransform; // float4x4

    // Buffer offsets (for multi-camera processing)
    public uint depthDataOffset;
    public uint lutDataOffset;
    public uint outputOffset;
}

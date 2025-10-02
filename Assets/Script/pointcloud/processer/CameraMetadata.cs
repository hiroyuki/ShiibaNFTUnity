using UnityEngine;

public struct CameraMetadata
{
    // Transform matrices
    public Matrix4x4 d2cRotation;
    public Vector3 d2cTranslation;
    public Matrix4x4 depthViewerTransform;

    // Camera intrinsics
    public float fx_d, fy_d, cx_d, cy_d; // Depth camera: fx, fy, cx, cy
    public float fx_c, fy_c, cx_c, cy_c; // Color camera: fx, fy, cx, cy

    // Distortion parameters
    public float k1_d, k2_d, k3_d, k4_d, k5_d, k6_d, p1_d, p2_d; // Depth distortion: k1~k6, p1, p2
    public float k1_c, k2_c, k3_c, k4_c, k5_c, k6_c, p1_c, p2_c; // Color distortion: k1~k6, p1, p2

    // Image dimensions
    public uint depthWidth;
    public uint depthHeight;
    public uint colorWidth;
    public uint colorHeight;

    // Processing parameters
    public float depthScaleFactor;
    public float depthBias;
    public int useOpenCVLUT;

    // Buffer offsets (for multi-camera processing)
    public uint depthDataOffset;
    public uint lutDataOffset;
    public uint outputOffset;
}

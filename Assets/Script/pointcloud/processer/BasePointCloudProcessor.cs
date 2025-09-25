using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Abstract base class that contains all shared functionality between point cloud processors
/// This eliminates code duplication and ensures consistent behavior
/// </summary>
public abstract class BasePointCloudProcessor : IPointCloudProcessor
{
    // Common properties
    public abstract ProcessingType ProcessingType { get; }
    public string DeviceName { get; protected set; }

    // Common camera parameters
    protected int depthWidth, depthHeight;
    protected int colorWidth, colorHeight;
    protected float[] depthIntrinsics; // Depth camera: fx, fy, cx, cy
    protected float[] colorIntrinsics; // Color camera: fx, fy, cx, cy
    protected float[] colorDistortion; // k1～k6, p1, p2
    protected float[] depthDistortion; // k1～k6, p1, p2
    protected Vector2[,] depthUndistortLUT;
    protected float depthScaleFactor;
    protected float depthBias;

    // Common transform parameters
    protected Quaternion rotation = Quaternion.identity;
    protected Vector3 translation = Vector3.zero;
    protected Transform boundingVolume;
    protected Transform depthViewerTransform;

    protected struct CameraParameters
    {
        public float fx_d, fy_d, cx_d, cy_d; // Depth camera
        public float fx_c, fy_c, cx_c, cy_c; // Color camera
    } 
    protected CameraParameters cameraParams;

    protected BasePointCloudProcessor(string deviceName)
    {
        DeviceName = deviceName;
    }

    // Abstract methods that must be implemented by concrete classes
    public abstract bool IsSupported();
    public abstract void UpdateMesh(Mesh mesh, SensorDevice device);

    // Common interface implementations
    public virtual void Setup(SensorDevice device, float depthBias)
    {
        this.depthScaleFactor = device.GetDepthScaleFactor();
        this.depthBias = depthBias;

        SetupDepthCamera(device.GetDepthParser().sensorHeader);
        SetupColorCamera(device.GetColorParser().sensorHeader);
        SetupCameraParameters();
    }

    public virtual void SetDepthViewerTransform(Transform transform)
    {
        depthViewerTransform = transform;
    }

    public virtual void SetBoundingVolume(Transform boundingVolume)
    {
        this.boundingVolume = boundingVolume;
    }

    public virtual void ApplyDepthToColorExtrinsics(Vector3 translation, Quaternion rotation)
    {
        this.translation = translation;
        this.rotation = rotation;
    }

    public virtual void SetupColorIntrinsics(SensorHeader colorHeader)
    {
        // Already handled in Setup method for most processors
        // Override if specific processor needs different behavior
    }

    // Common protected methods for shared functionality
    protected virtual void SetupDepthCamera(SensorHeader depthHeader)
    {
        
        depthWidth = depthHeader.custom.camera_sensor.width;
        depthHeight = depthHeader.custom.camera_sensor.height;
        // Parse depth camera intrinsics
        var depthParams = ParseIntrinsics(depthHeader.custom.additional_info.orbbec_intrinsics_parameters);
        this.depthIntrinsics = depthParams.Take(4).ToArray(); // fx, fy, cx, cy
        this.depthDistortion = depthParams.Skip(4).ToArray(); // k1~k6, p1, p2


        this.depthUndistortLUT = OpenCVUndistortHelper.BuildUndistortLUTFromHeader(depthHeader);
    }

    protected virtual void SetupColorCamera(SensorHeader colorHeader)
    {
        colorIntrinsics = ParseIntrinsics(colorHeader.custom.additional_info.orbbec_intrinsics_parameters);
        colorWidth = colorHeader.custom.camera_sensor.width;
        colorHeight = colorHeader.custom.camera_sensor.height;

        // fx, fy, cx, cy: 最初の4要素
        // distortion: 残り8要素（k1~k6, p1, p2）
        this.colorIntrinsics = colorIntrinsics.Take(4).ToArray(); // 4要素
        this.colorDistortion = colorIntrinsics.Skip(4).ToArray(); // 8要素
    }

    // Check if point is within bounding volume (common utility)
    protected virtual bool IsPointInBounds(Vector3 point)
    {
        if (PointCloudSettings.showAllPoints || boundingVolume == null)
            return true;

        // Transform point to bounding volume local space
        Vector3 localPoint = boundingVolume.InverseTransformPoint(point);

        // Simple box bounds check (can be overridden for more complex shapes)
        return Mathf.Abs(localPoint.x) <= 0.5f &&
               Mathf.Abs(localPoint.y) <= 0.5f &&
               Mathf.Abs(localPoint.z) <= 0.5f;
    }

        
    private float[] ParseIntrinsics(string param)
    {
        return param.Trim('[', ']').Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(float.Parse).ToArray();
    }

    private void SetupCameraParameters()
    {
        cameraParams = new CameraParameters
        {
            fx_d = depthIntrinsics[0], fy_d = depthIntrinsics[1], cx_d = depthIntrinsics[2], cy_d = depthIntrinsics[3],
            fx_c = colorIntrinsics[0], fy_c = colorIntrinsics[1], cx_c = colorIntrinsics[2], cy_c = colorIntrinsics[3]
        };
    }

    // Template method for common cleanup
    public virtual void Dispose()
    {
        // Base cleanup - override and call base.Dispose() in derived classes
        depthUndistortLUT = null;
        depthIntrinsics = null;
        colorIntrinsics = null;
        colorDistortion = null;
        depthDistortion = null;
    }
}
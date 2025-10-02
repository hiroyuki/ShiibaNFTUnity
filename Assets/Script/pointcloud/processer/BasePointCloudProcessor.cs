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
    protected Quaternion d2cRotation = Quaternion.identity;
    protected Vector3 d2cTranslation = Vector3.zero;
    protected Transform boundingVolume;
    protected Transform depthViewerTransform;


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

        // Get camera parameters directly from SensorDevice (centralized)
        this.depthWidth = device.GetDepthWidth();
        this.depthHeight = device.GetDepthHeight();
        this.colorWidth = device.GetColorWidth();
        this.colorHeight = device.GetColorHeight();
        this.depthIntrinsics = device.GetDepthIntrinsics();
        this.colorIntrinsics = device.GetColorIntrinsics();
        this.depthDistortion = device.GetDepthDistortion();
        this.colorDistortion = device.GetColorDistortion();
        this.depthUndistortLUT = device.GetDepthUndistortLUT();
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
        this.d2cTranslation = translation;
        this.d2cRotation = rotation;
    }

    // Alternative method to get transforms from SensorDevice
    protected void LoadTransformsFromDevice(SensorDevice device)
    {
        this.d2cTranslation = device.GetDepthToColorTranslation();
        this.d2cRotation = device.GetDepthToColorRotation();
    }

    public virtual void SetupColorIntrinsics(SensorHeader colorHeader)
    {
        // Already handled in Setup method for most processors
        // Override if specific processor needs different behavior
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


     public virtual CameraMetadata SetupCameraMetadata(SensorDevice device)
    {
        // Get metadata from device
        CameraMetadata metadata = device.CreateCameraMetadata(depthViewerTransform, boundingVolume, PointCloudSettings.showAllPoints);
        return metadata;
    }
}
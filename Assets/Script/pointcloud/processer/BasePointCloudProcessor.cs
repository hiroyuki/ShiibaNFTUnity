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

    // Alternative method to get transforms from SensorDevice
    protected void LoadTransformsFromDevice(SensorDevice device)
    {
        this.translation = device.GetDepthToColorTranslation();
        this.rotation = device.GetDepthToColorRotation();
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
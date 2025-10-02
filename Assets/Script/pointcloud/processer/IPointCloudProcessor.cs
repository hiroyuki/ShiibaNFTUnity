using UnityEngine;
using System;

public interface IPointCloudProcessor : IDisposable
{
    string DeviceName { get; }
    bool IsSupported();

    void Setup(SensorDevice device, float depthBias);
    void SetDepthViewerTransform(Transform transform);
    void SetBoundingVolume(Transform boundingVolume);
    void ApplyDepthToColorExtrinsics(Vector3 translation, Quaternion rotation);
    void SetupColorIntrinsics(SensorHeader colorHeader);

    void UpdateMesh(Mesh mesh, SensorDevice device);
}
# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ShiibaNFTUnity is a real-time 3D point cloud visualization and animation system built with **Unity 6000.1.10f1** and **C#**. It processes multi-camera sensor data (depth + color from multiple cameras), applies computer vision operations (OpenCV integration), and synchronizes skeletal animation (BVH format) with point cloud playback via the Unity Timeline system.

**Key Technologies:**
- Game Engine: Unity 6000.1.10f1 with Universal Render Pipeline (URP)
- Language: C# 11+
- Computer Vision: OpenCV for Unity
- Animation: Timeline system (1.8.7) + BVH skeletal animation
- Configuration: YamlDotNet, ScriptableObjects
- Data Formats: PLY (text/binary), custom binary, BVH, YAML

## Architecture Overview

The codebase implements a **multi-layered, pattern-based architecture** designed for processing and visualizing multi-camera point cloud data:

```
Timeline/Animation Layer
        ↓
Configuration Layer (DatasetConfig ScriptableObject)
        ↓
Manager/Orchestrator Layer (MultiCameraPointCloudManager)
        ↓
Processing Mode Handler Layer (PlyModeHandler, BinaryModeHandler)
        ↓
Processing Implementations (CPU/GPU/OneShader processors)
        ↓
Device Abstraction Layer (SensorDevice with multi-parser support)
        ↓
Rendering/View Layer (SinglePointCloudView, MultiPointCloudView)
```

### Design Patterns

- **Factory Pattern:** `SensorDataParserFactory`, `PointCloudProcessorFactory` - enables pluggable implementations
- **Strategy Pattern:** `IProcessingModeHandler`, `IPointCloudProcessor` - allows runtime selection of processing strategies
- **Abstract Base Classes:** `BasePointCloudProcessor`, `AbstractSensorDataParser` - provides common functionality for implementations
- **Observer/Event Pattern:** Timeline playables synchronize point cloud and skeleton animation
- **ScriptableObject Pattern:** `DatasetConfig` serves as inspector-editable, serializable configuration container

### Key Architectural Points

1. **Centralized Configuration:** `DatasetConfig` ScriptableObject manages all dataset paths, processing modes, BVH parameters, and view settings. This is the single source of truth for dataset-specific configuration.

2. **Multi-Camera Support:** The `SensorDevice` layer abstracts multiple sensor inputs. `MultiCameraPointCloudManager` orchestrates fusion of data from multiple cameras with proper synchronization.

3. **Pluggable Processing:** The processor factory allows selection between CPU (`CPUPointCloudProcessor`), GPU (`GPUPointCloudProcessor`), and multi-camera GPU (`MultiCameraGPUProcessor`) implementations. New processors can be added by extending `BasePointCloudProcessor` and registering in the factory.

4. **Data Format Flexibility:** `PlyModeHandler` and `BinaryModeHandler` provide different data ingestion paths. New handlers can be added by extending `BaseProcessingModeHandler`.

5. **Timeline Integration:** `TimelineController`, `PointCloudPlayableAsset`, and `BvhPlayableAsset` integrate with Unity's Timeline system, allowing precise synchronization between point cloud animation and skeletal animation across multiple playback channels.

## Directory Structure

```
Assets/Script/
├── config/
│   └── DatasetConfig.cs                 # Central configuration ScriptableObject
│
├── device/                              # Sensor abstraction & data parsing
│   ├── SensorDevice.cs                  # Main sensor orchestration
│   ├── SensorDataParserFactory.cs       # Factory for parser selection
│   ├── RcstSensorDataParser.cs          # Depth data parser
│   ├── RcsvSensorDataParser.cs          # Color data parser
│   ├── AbstractSensorDataParser.cs      # Base class for parsers
│   ├── ISensorDataParser.cs             # Parser interface
│   ├── CameraMetadata.cs                # Camera intrinsics/extrinsics
│   └── [metadata classes]
│
├── pointcloud/                          # Core point cloud processing
│   ├── MultiCamPointCloudManager.cs     # Main orchestrator
│   ├── processer/                       # Processing implementations
│   │   ├── IPointCloudProcessor.cs      # Processor interface
│   │   ├── BasePointCloudProcessor.cs   # Abstract base
│   │   ├── CPUPointCloudProcessor.cs
│   │   ├── GPUPointCloudProcessor.cs
│   │   ├── MultiCameraGPUProcessor.cs
│   │   └── PointCloudProcessorFactory.cs
│   ├── handler/                         # Processing mode selection
│   │   ├── IProcessingModeHandler.cs
│   │   ├── BaseProcessingModeHandler.cs
│   │   ├── PlyModeHandler.cs            # PLY file ingestion
│   │   └── BinaryModeHandler.cs         # Binary format ingestion
│   ├── controller/                      # Frame control logic
│   │   ├── IFrameController.cs
│   │   ├── CameraFrameController.cs
│   │   └── PlyFrameController.cs
│   ├── manager/                         # High-level management
│   │   ├── FrameProcessingManager.cs
│   │   └── PlyExportManager.cs
│   └── view/                            # Rendering & visualization
│       ├── SinglePointCloudView.cs
│       ├── MultiPointCloudView.cs
│       └── PointCloudSettings.cs
│
├── timeline/                            # Timeline integration
│   ├── TimelineController.cs            # Main timeline orchestration
│   ├── PointCloudPlayableAsset.cs       # Playable asset for point clouds
│   ├── PointCloudPlayableBehaviour.cs
│   ├── BvhPlayableAsset.cs              # Playable asset for skeleton
│   └── BvhPlayableBehaviour.cs
│
└── utils/                               # Utility functions
    ├── PlyImporter.cs                   # PLY format import
    ├── PlyExporter.cs                   # PLY format export
    ├── BvhImporter.cs                   # BVH skeletal animation import
    ├── BvhPlayer.cs                     # BVH playback controller
    ├── BvhData.cs                       # BVH data structures
    ├── BvhSkeletonVisualizer.cs         # Skeleton rendering
    ├── ExtrinsicsLoader.cs              # Camera extrinsics loading
    ├── YamlLoader.cs                    # YAML config parsing
    ├── OpenCVUndistortHelper.cs         # Camera undistortion
    ├── UndistortLutGenerator.cs         # LUT generation
    ├── BoundingVolumeGizmo.cs           # Debug visualization
    └── [other utilities]
```

## Development Commands

### Opening & Building
- **Primary Workflow:** Open the project in Unity Editor 6000.1.10f1. Use the Timeline editor to create/modify animation sequences, and the Inspector to configure `DatasetConfig` ScriptableObjects.
- **C# Development:** Open `ShiibaNFTUnity.sln` in Visual Studio Code for code editing and compilation.

### Timeline Playback Controls
- **Play/Pause:** Space bar
- **Stop:** Escape key
- **Camera Control:** Mouse orbit controls (via `MouseOrbitCamera.cs`)

### Configuration
- **Dataset Setup:** Create or edit `DatasetConfig` ScriptableObjects via the Inspector
- **Processing Mode:** Set via `DatasetConfig.processingMode` (PLY or Binary)
- **Processor Selection:** Set via `DatasetConfig.processorType` (CPU, GPU, or MultiCamera GPU)
- **View Settings:** Configure point cloud visualization via `PointCloudSettings`

### Debugging
- **VS Code:** Use `.vscode/launch.json` configuration to debug in Unity Editor

### Build Targets
- PC/Mac/Linux Standalone: Available in Build Settings
- WebGL: Available in Build Settings
- Mobile: Android/iOS configured in PackageManager

## Important Implementation Patterns

### Adding a New Point Cloud Processor

1. Extend `BasePointCloudProcessor<T>` (CPU) or `BasePointCloudProcessor<T>` (GPU) with your processing logic
2. Implement `Process(RawPointCloudData data, PointCloudSettings settings)` method
3. Register in `PointCloudProcessorFactory.CreateProcessor()` method
4. Add processor type to configuration enum and expose in `DatasetConfig`

### Adding a New Sensor Data Parser

1. Extend `AbstractSensorDataParser` or implement `ISensorDataParser`
2. Implement frame reading and data conversion methods
3. Register in `SensorDataParserFactory.CreateParser()` method
4. Configure sensor type in `DatasetConfig` or hardware setup

### Adding a New Processing Mode Handler

1. Extend `BaseProcessingModeHandler` with your handling logic
2. Implement `IFrameController` for frame sequencing
3. Register in `MultiCameraPointCloudManager.InitializeHandler()` method
4. Expose mode selection in `DatasetConfig.processingMode`

### Synchronizing Timeline with Point Cloud/Skeleton

Timeline synchronization works via PlayableAsset/PlayableBehaviour pattern:
- `PointCloudPlayableAsset` and `BvhPlayableAsset` are registered on Timeline tracks
- `TimelineController` manages playback state across both tracks
- Seek operations automatically synchronize both data sources via `OnGraphStart()` callbacks
- Use Timeline editor to adjust relative timing and duration

## Multi-Camera Point Cloud Fusion

The system handles multi-camera sensor fusion through:

1. **Device Abstraction:** `SensorDevice` manages multiple sensor inputs (depth + color per camera)
2. **Parser Coordination:** `SensorDataParserFactory` creates appropriate parsers for each sensor type
3. **Frame Synchronization:** `MultiCameraPointCloudManager` coordinates frame acquisition across cameras
4. **GPU Processing:** `MultiCameraGPUProcessor` fuses point clouds in GPU memory for performance
5. **Camera Calibration:** `ExtrinsicsLoader` and `OpenCVUndistortHelper` apply camera intrinsics/extrinsics

## Git Context

- **Current Branch:** BVH-ALIGN (active development on BVH alignment features)
- **Main Branch:** main (use for pull requests)
- **Recent Focus:** BVH import, timeline synchronization, and point cloud sequence loading

## Known Issues

### BVH Skeleton Visualization Not Displaying (2025-11-11)

**Status:** Under Investigation

**Symptom:** BVH_Visuals GameObject is created under BVH_Character, but the skeleton visualization (joint spheres and bone lines) is not visible in the viewport.

**Diagnosis:**
- BvhPlayableBehaviour creates the joint hierarchy (via `CreateJointHierarchy()`) during Timeline `OnGraphStart()`
- BvhSkeletonVisualizer attempts to visualize joints but finds them empty
- Root cause: Timing issue - joint creation may be delayed, or BvhSkeletonVisualizer timing needs adjustment
- Delay increased from 0.2s → 1.0s in `BvhSkeletonVisualizer.Start()` (2025-11-11), but issue persists

**Files Involved:**
- `Assets/Script/timeline/BvhPlayableBehaviour.cs` - Creates joint hierarchy in `OnGraphStart()` (Line 37)
- `Assets/Script/utils/BvhSkeletonVisualizer.cs` - Attempts visualization via `Invoke(CreateVisuals, 1.0f)` (Line 47)

**Next Steps:**
1. Add more detailed debug logging to track joint creation timing
2. Consider event-based callback from BvhPlayableBehaviour instead of Invoke timing
3. Verify joint hierarchy is properly created before visualization attempt

## Additional Resources

- **README.md:** Project overview
- **In-Code Documentation:** All major classes include extensive XML documentation comments

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Instruction
When the user writes a prompt in English, rewrite it into more natural, fluent English without changing its meaning, then repeat the rewritten version back to the user.
Do not explain or justify changes unless explicitly asked. Only show the improved version.


## Project Overview

ShiibaNFTUnity is a real-time 3D point cloud visualization and animation system built with **Unity 6000.1.10f1** and **C#**. It processes multi-camera sensor data (depth + color from multiple cameras), applies computer vision operations (OpenCV integration), and synchronizes skeletal animation (BVH format) with point cloud playback via the Unity Timeline system.1

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
├── bvh/                                 # Skeletal animation (BVH format)
│   ├── BvhData.cs                       # Core BVH data structures
│   ├── BvhImporter.cs                   # BVH file import
│   ├── BvhDataReader.cs                 # Parses channel data from frame arrays
│   ├── BvhDataCache.cs                  # Centralized BVH data management
│   ├── BvhMotionApplier.cs               # Applies frame data to joint hierarchy (extensible)
│   ├── BvhKeyframe.cs                   # Keyframe data structure
│   ├── BvhJointHierarchyBuilder.cs      # Creates joint hierarchies (frame-agnostic, idempotent)
│   ├── BvhSkeletonVisualizer.cs         # Skeleton joint/bone rendering
│   ├── datacorrection/                  # Playback correction system (frame mapping + transform correction)
│   │   ├── BvhPlaybackFrameMapper.cs       # Maps Timeline time → BVH frame (with keyframe interpolation)
│   │   ├── BvhPlaybackTransformCorrector.cs # Calculates corrected root position/rotation
│   │   └── BvhPlaybackCorrectionKeyframes.cs # Unified keyframe container for all corrections
│   └── Editor/
│       └── BvhDriftCorrectionDataEditor.cs  # Custom inspector UI for correction keyframes
│
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
│   ├── SensorHeader.cs                  # Sensor stream header data
│   ├── DatasetInfo.cs                   # Dataset metadata
│   └── HostInfo.cs                      # Host/environment information
│
├── pointcloud/                          # Core point cloud processing
│   ├── MultiCamPointCloudManager.cs     # Main orchestrator
│   ├── controller/                      # Frame control logic
│   │   ├── IFrameController.cs
│   │   ├── CameraFrameController.cs
│   │   └── PlyFrameController.cs
│   ├── handler/                         # Processing mode selection
│   │   ├── IProcessingModeHandler.cs
│   │   ├── BaseProcessingModeHandler.cs
│   │   ├── PlyModeHandler.cs            # PLY file ingestion
│   │   └── BinaryModeHandler.cs         # Binary format ingestion
│   ├── manager/                         # High-level management
│   │   ├── FrameProcessingManager.cs
│   │   └── PlyExportManager.cs
│   ├── processer/                       # Processing implementations
│   │   ├── IPointCloudProcessor.cs      # Processor interface
│   │   ├── BasePointCloudProcessor.cs   # Abstract base
│   │   ├── CPUPointCloudProcessor.cs
│   │   ├── GPUPointCloudProcessor.cs
│   │   ├── MultiCameraGPUProcessor.cs
│   │   └── PointCloudProcessorFactory.cs
│   └── view/                            # Rendering & visualization
│       ├── SinglePointCloudView.cs
│       ├── MultiPointCloudView.cs
│       └── PointCloudSettings.cs
│
├── sceneflow/                           # Scene flow calculation
│   ├── SceneFlowCalculator.cs           # Scene flow computation
│   └── Editor/
│       └── SceneFlowCalculatorEditor.cs # Custom editor UI
│
├── timeline/                            # Timeline integration
│   ├── TimelineController.cs            # Main timeline orchestration
│   ├── PointCloudPlayableAsset.cs       # Playable asset for point clouds
│   ├── PointCloudPlayableBehaviour.cs
│   ├── BvhPlayableAsset.cs              # Playable asset for skeleton
│   ├── BvhPlayableBehaviour.cs
│   └── BVH_TIMELINE_USAGE.md            # Comprehensive BVH timeline guide
│
└── utils/                               # Utility functions
    ├── PlyImporter.cs                   # PLY format import
    ├── PlyExporter.cs                   # PLY format export
    ├── BvhImporter.cs                   # BVH format import
    ├── ExtrinsicsLoader.cs              # Camera extrinsics loading
    ├── YamlLoader.cs                    # YAML config parsing
    ├── OpenCVUndistortHelper.cs         # Camera undistortion
    ├── UndistortLutGenerator.cs         # LUT generation
    ├── BoundingVolumeGizmo.cs           # Debug visualization
    ├── BoundingVolumeDebugController.cs # Bounding volume debug controller
    ├── MouseOrbitCamera.cs              # Camera orbit control
    ├── CameraPositionGizmo.cs           # Camera position visualization
    ├── SetupStatusUI.cs                 # Setup status UI
    └── Editor/
        └── (empty)
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

### Creating BVH Joint Hierarchies

Use the `BvhJointHierarchyBuilder` utility for creating skeleton hierarchies:

```csharp
// Create a complete skeleton hierarchy from BVH data
BvhData bvhData = GetBvhData();
Transform parentTransform = GameObject.Find("BVH_Character").transform;
Transform rootJoint = BvhJointHierarchyBuilder.CreateOrGetJointHierarchy(bvhData, parentTransform);

// Features:
// - Idempotent: Safe to call multiple times (existing joints are reused)
// - Frame-agnostic: Creates hierarchy structure; frame data applied separately
// - Timeline-independent: Can be used in any component, not just Timeline playback
```

**Benefits:**
- Decouples skeleton creation from Timeline lifecycle
- Enables reuse in SceneFlowCalculator, visualizers, and other components
- Single source of truth for hierarchy creation logic

### BVH Frame Handling System Architecture

The BVH frame application system is built on clear separation of concerns with specialized utility classes:

| Component | Responsibility | Key Methods | Used By |
|-----------|-----------------|-------------|---------|
| **BvhMotionApplier** | Convert BVH frame data to joint transforms | `ApplyFrameToJointHierarchy()` | BvhData, BvhPlayableBehaviour, SceneFlowCalculator |
| **BvhFrameMapper** | Map Timeline time → BVH frame index (with drift correction) | `GetTargetFrameForTime()` | BvhPlayableBehaviour |
| **BvhDataReader** | Parse channel data from frame arrays | `ReadChannelData()`, `GetRotationQuaternion()` | BvhMotionApplier |
| **BvhData** | Store BVH structure + frame data; provide frame access | `GetFrame()`, `UpdateTransforms()` | BvhPlayableBehaviour, offline processing |
| **BvhDriftCorrectionController** | Calculate drift-corrected root transform | `GetCorrectedRootPosition()`, `GetCorrectedRootRotation()` | BvhPlayableBehaviour |
| **BvhDriftCorrectionData** | Store keyframes; interpolate position/rotation at time | `GetAnchorPositionAtTime()`, `GetAnchorRotationAtTime()` | BvhFrameMapper, BvhDriftCorrectionController |

**Key Design Principles:**

1. **BvhMotionApplier is concrete and extensible**: Can be instantiated directly for basic usage, or subclassed to override `AdjustPosition()` and `AdjustRotation()` for custom behavior (e.g., BvhPlayableBehaviour's PlayableFrameApplier adds scale and rotation offsets).

2. **BvhFrameMapper is stateless and reusable**: All parameters are passed explicitly; no hidden dependencies. Can be instantiated fresh or cached without side effects.

3. **Timeline-independent utilities**: All core logic (BvhFrameMapper, BvhDriftCorrectionController, BvhMotionApplier) work outside Timeline context, enabling reuse in Scene Flow, offline processing, and other components.

4. **Single frame application point**: BvhMotionApplier is the authoritative implementation of frame-to-transform conversion, eliminating duplication across the codebase.

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

- **Current Branch:** DEPTH-FLOW (active development)
- **Main Branch:** main (use for pull requests)
- **Recent Commits:**
  - `82ba346` - refactoring
  - `ab77927` - key frame drift adjustment修正
  - `81be31f` - Show Scene Flowボタン追加とbone セグメンテーション（うまくいってない）
  - `243e12e` - Update TotoriBVHAdjustData.asset
  - `d5c5566` - WIP
- **Recent Focus:** BVH skeletal animation refinements, scene flow visualization, point cloud processing improvements

## Known Issues

### BVH Skeleton Visualization Not Displaying (2025-11-11)

**Status:** Under Investigation (Recent refactoring in progress)

**Symptom:** BVH_Visuals GameObject is created under BVH_Character, but the skeleton visualization (joint spheres and bone lines) is not visible in the viewport.

**Diagnosis:**
- BvhPlayableBehaviour creates the joint hierarchy via `BvhJointHierarchyBuilder.CreateOrGetJointHierarchy()` during Timeline `OnGraphStart()`
- BvhSkeletonVisualizer attempts to visualize joints but may encounter timing issues
- Root cause: Timing issue between joint creation and visualization attempt
- Delay increased from 0.2s → 1.0s in `BvhSkeletonVisualizer.Start()`, but issue persists
- Recent refactoring (commit 82ba346) may have addressed this

**Files Involved:**
- [Assets/Script/timeline/BvhPlayableBehaviour.cs](Assets/Script/timeline/BvhPlayableBehaviour.cs) - Calls joint hierarchy builder in `OnGraphStart()`
- [Assets/Script/bvh/BvhJointHierarchyBuilder.cs](Assets/Script/bvh/BvhJointHierarchyBuilder.cs) - Creates joint hierarchy (utility class)
- [Assets/Script/bvh/BvhSkeletonVisualizer.cs](Assets/Script/bvh/BvhSkeletonVisualizer.cs) - Visualization logic
- [Assets/Script/bvh/BvhMotionApplier.cs](Assets/Script/bvh/BvhMotionApplier.cs) - Frame application (new component)

**Next Steps:**
1. Test visualization after recent refactoring
2. If still failing, add event-based callback from BvhPlayableBehaviour instead of Invoke timing
3. Verify joint hierarchy is properly created before visualization attempt

### Bone Segmentation Issue (2025-11-11)

**Status:** In Progress (うまくいってない - not working well)

**Symptom:** Bone segmentation feature (referenced in scene flow button additions) is not functioning correctly.

**Files Involved:**
- [Assets/Script/sceneflow/SceneFlowCalculator.cs](Assets/Script/sceneflow/SceneFlowCalculator.cs) - Scene flow calculation
- [Assets/Script/sceneflow/Editor/SceneFlowCalculatorEditor.cs](Assets/Script/sceneflow/Editor/SceneFlowCalculatorEditor.cs) - Editor UI

**Recent Focus:** Debugging bone segmentation algorithm; see commit 81be31f for latest attempts.

## Recent Additions & Updates

### New BVH System (bvh/ directory)
The BVH skeletal animation system has been significantly enhanced with new dedicated classes:
- **BvhChannelReader.cs** - Parses BVH motion data channels
- **BvhKeyframe.cs** - Represents individual animation keyframes
- **BvhMotionApplier.cs** - Applies keyframe data to skeleton hierarchy
- **BvhJointHierarchyBuilder.cs** - Static utility for creating and managing joint hierarchies (frame-agnostic, idempotent)
- **BvhDriftCorrectionData.cs** - Corrects animation drift/misalignment issues
- Custom inspector editor for drift correction parameters

This modular approach improves maintainability and allows fine-grained control over BVH animation playback.

#### Joint Hierarchy Builder (New - 2025-12-03)
**BvhJointHierarchyBuilder** is a new static utility class that extracts joint hierarchy creation logic from BvhPlayableBehaviour. Key features:
- **Reusable**: Any component can create joint hierarchies without Timeline dependency
- **Frame-agnostic**: Creates skeleton structure only; frame data applied separately via BvhMotionApplier
- **Idempotent**: Safe to call multiple times without duplicating GameObjects
- **Usage**: `BvhJointHierarchyBuilder.CreateOrGetJointHierarchy(bvhData, parentTransform)`
- **Benefits**: Decouples hierarchy creation from Timeline lifecycle; enables use in SceneFlowCalculator and other components

### Scene Flow System (sceneflow/ directory)
New scene flow calculation system for optical flow/motion visualization:
- **SceneFlowCalculator.cs** - Core computation engine
- **SceneFlowCalculatorEditor.cs** - Custom editor UI for configuration
- Provides "Show Scene Flow" button for debugging point cloud motion

**Status:** Currently under development; bone segmentation component not yet fully functional.

### Enhanced Debugging Tools (utils/)
Added new debugging and visualization components:
- **BoundingVolumeDebugController.cs** - Runtime control of bounding volume display
- **CameraPositionGizmo.cs** - Visual indicators for camera positions
- **SetupStatusUI.cs** - Status display for project initialization

### Timeline Documentation
- **BVH_TIMELINE_USAGE.md** - Comprehensive guide for integrating BVH animation with Timeline system, including:
  - Setup and configuration steps
  - Transform adjustment procedures
  - Timeline synchronization with point clouds
  - Programmatic control examples
  - Troubleshooting guidance

## Additional Resources

- **README.md:** Project overview
- **BVH_TIMELINE_USAGE.md:** [Assets/Script/timeline/BVH_TIMELINE_USAGE.md](Assets/Script/timeline/BVH_TIMELINE_USAGE.md) - Detailed BVH timeline integration guide
- **In-Code Documentation:** All major classes include extensive XML documentation comments

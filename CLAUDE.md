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
│   ├── BvhData.cs                       # Core BVH data structures & frame application
│   ├── BvhImporter.cs                   # BVH file import
│   ├── BvhDataReader.cs                 # Parses channel data from frame arrays (static utility)
│   ├── BvhDataCache.cs                  # Centralized BVH data management
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
│       ├── SinglePointCloudView.cs      # Single camera point cloud viewer
│       ├── MultiPointCloudView.cs       # Multi-camera unified viewer
│       ├── RuntimeMotionVectorVisualizer.cs # Runtime motion vector visualization
│       └── PointCloudSettings.cs
│
├── sceneflow/                           # Scene flow calculation
│   ├── SceneFlowCalculator.cs           # Scene flow computation
│   ├── SceneFlowBatchExporter.cs        # Batch PLY export with scene flow vectors
│   ├── BoneSegmentData.cs               # Bone segmentation data structures
│   └── Editor/
│       └── SceneFlowCalculatorEditor.cs # Custom editor UI
│
├── timeline/                            # Timeline integration
│   ├── TimelineController.cs            # Main timeline orchestration
│   ├── PointCloudPlayableAsset.cs       # Playable asset for point clouds
│   ├── PointCloudPlayableBehaviour.cs
│   ├── BvhPlayableAsset.cs              # Playable asset for skeleton
│   ├── BvhPlayableBehaviour.cs
│   ├── BvhTransformSync.cs              # Syncs BVH_Character transform with DatasetConfig
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
    ├── DebugImageExporter.cs            # Debug image export utility
    ├── TimelineUtil.cs                  # Timeline utility functions
    └── Editor/
        └── (empty)

Assets/Script/Editor/                   # Editor-only tools
├── MotionVectorPLYGenerator.cs          # Generate PLY files with motion vectors
├── MotionVectorPLYValidator.cs          # Validate motion vector PLY files
└── PlyMotionVectorTest.cs               # Test motion vector calculations
```

## Scene Hierarchy Structure

The typical runtime scene hierarchy follows this structure:

```
SampleScene (or your scene name)
├── Main Camera
├── Directional Light
├── PointCloudTimeline           # Timeline controller
├── BoundingVolume               # Debug visualization
├── ConfigManager                # Configuration management
├── EventSystem
└── world                        # Root transform for all runtime objects
    ├── MultiCameraPointCloud    # Multi-camera orchestration
    │   └── MultiPointCloudView_PLY  # Point cloud view (has MultiPointCloudView component)
    │       └── UnifiedPointCloudViewer  # Unified mesh (has MeshFilter with combined point cloud)
    ├── SceneFlow                # Scene flow / motion vector calculation
    │   ├── CurrentFrameBVH      # Current frame skeleton
    │   │   └── TempBvhSkeleton_N
    │   └── PreviousFrameBVH     # Previous frame skeleton
    │       └── TempBvhSkeleton_N-1
    └── BVH_Character            # Main BVH character visualization
        ├── root                 # BVH root joint hierarchy
        └── BVH_Visuals          # Skeleton visualization (has BvhSkeletonVisualizer)
```

**Key Hierarchy Notes:**

1. **world GameObject**: Root container positioned at (0.614, -4.747, 4.811) to offset camera space coordinates
2. **MultiPointCloudView_PLY**: Positioned at (-0.614, 4.747, -4.811) to cancel out world offset, making local space = world space
3. **UnifiedPointCloudViewer**: Contains the actual combined point cloud mesh from all cameras (MeshFilter.sharedMesh)
4. **SceneFlow**: Contains scene flow calculation components and temporary skeleton instances for motion vector computation
5. **BVH_Character**: Main character skeleton created by Timeline playback

**Component Locations:**
- `MultiCameraPointCloudManager`: On `MultiCameraPointCloud` GameObject
- `MultiPointCloudView`: On `MultiPointCloudView_PLY` GameObject
- `SceneFlowCalculator`: On `SceneFlow` GameObject
- `BvhSkeletonVisualizer`: On `BVH_Visuals` GameObject under `BVH_Character`

**Coordinate Space:**
- Point cloud vertices are in camera space by default
- The world/MultiPointCloudView_PLY offset hierarchy ensures local vertex positions align with world space bone positions
- This allows direct nearest-neighbor search without coordinate transformation

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

## Data Setup Guide

This section explains how to configure point cloud data and BVH skeletal animation data for playback in Unity.

### Dataset Folder Structure

All datasets should follow this standardized folder structure:

```
Assets/Data/Datasets/{DatasetName}/
├── BVH/                      # BVH skeletal animation files
│   └── motion.bvh            # BVH file (auto-detected)
├── PLY/                      # Pre-exported PLY point cloud files
│   ├── frame_0000.ply
│   ├── frame_0001.ply
│   └── ...
├── PLY_WithMotion/           # PLY files with embedded motion vectors (optional)
│   ├── frame_0000.ply        # Contains motion vector data
│   ├── frame_0001.ply
│   └── ...
└── Config/                   # Dataset-specific configuration (optional)
    └── camera_extrinsics.yaml
```

**For Binary Data (External Datasets):**
Binary sensor data can be stored **outside** the Unity project (e.g., on external drives):
```
{ExternalPath}/Datasets/{DatasetName}/
├── depth/                    # Raw depth data (.rcst files)
│   ├── camera_0/
│   ├── camera_1/
│   └── ...
├── color/                    # Raw color data (.rcsv files)
│   ├── camera_0/
│   ├── camera_1/
│   └── ...
└── Config/                   # Camera calibration
    └── camera_extrinsics.yaml
```

### Creating a DatasetConfig ScriptableObject

1. **Create a new DatasetConfig asset:**
   - Right-click in the Project window
   - Select `Create > Shiiba > DatasetConfig`
   - Name it descriptively (e.g., `TotoriDatasetConfig`)

2. **Configure the DatasetConfig in Inspector:**

   **Dataset Folder:**
   - Drag the dataset folder from `Assets/Data/Datasets/{DatasetName}/` into the **Dataset Folder** field
   - This automatically sets up relative paths for BVH and PLY data

   **BVH Configuration:**
   - **Enable BVH**: Check this to enable skeletal animation playback
   - **BVH File**: (Optional) Drag a specific `.bvh` file, or leave empty for auto-detection from `BVH/` folder
   - **BVH Position Offset**: Offset to align skeleton with point cloud (e.g., `(0.614, -4.747, 4.811)`)
   - **BVH Rotation Offset**: Rotation correction (e.g., `(0, 180, 0)` to flip facing direction)
   - **BVH Scale**: Scale factor for skeleton size (default: `(1, 1, 1)`)

   **Processing Mode:**
   Select one of the following processing types:

   | Processing Type | Description | Use Case | Data Location |
   |----------------|-------------|----------|---------------|
   | **PLY** | Use pre-exported PLY files from `PLY/` folder | Fastest playback; recommended for visualization | `{DatasetFolder}/PLY/` |
   | **PLY_WITH_MOTION** | Use PLY files with embedded motion vectors from `PLY_WithMotion/` folder | Scene flow visualization and analysis | `{DatasetFolder}/PLY_WithMotion/` |
   | **CPU** | Process raw binary sensor data on CPU | Development/debugging; slower | External binary path |
   | **GPU** | Process raw binary sensor data on GPU | Real-time multi-camera fusion | External binary path |
   | **ONESHADER** | GPU processing with unified shader | Experimental high-performance mode | External binary path |

   **Binary Data Configuration** (only for CPU/GPU/ONESHADER modes):
   - **Binary Data Root Path**: Full path to external binary dataset (e.g., `D:/Datasets/Totori/`)
   - This path is NOT inside the Unity Assets folder and can be on external drives

   **BVH Drift Correction** (optional):
   - **BVH Drift Correction Data**: Reference to `BvhPlaybackCorrectionKeyframes` ScriptableObject
   - Used to manually correct skeleton position/rotation drift over time via keyframes
   - Create via `Create > Shiiba > BvhPlaybackCorrectionKeyframes`

   **Point Cloud Downsampling** (optional):
   - **Show Downsampled Point Cloud**: Toggle to visualize downsampled point cloud
   - Requires `PointCloudDownsampler` component in the scene

### Setting Up Point Cloud Data

#### Option 1: PLY Mode (Recommended)

**Requirements:**
- Pre-exported PLY files in `{DatasetFolder}/PLY/` directory
- Files named sequentially: `frame_0000.ply`, `frame_0001.ply`, etc.

**Setup Steps:**
1. Export point cloud frames to PLY format using the `PlyExportManager`
2. Place PLY files in `Assets/Data/Datasets/{DatasetName}/PLY/`
3. In DatasetConfig Inspector, set **Processing Mode** to `PLY`
4. Set **Dataset Folder** to point to your dataset folder

**Advantages:**
- Fastest playback (no real-time processing)
- Works entirely within Unity Assets
- Suitable for visualization and presentation

#### Option 2: PLY_WITH_MOTION Mode

**Requirements:**
- PLY files with embedded motion vector data in `{DatasetFolder}/PLY_WithMotion/`
- Files contain additional properties: `mvx`, `mvy`, `mvz` (motion vectors)

**Setup Steps:**
1. Generate PLY files with motion vectors using `SceneFlowBatchExporter` or `MotionVectorPLYGenerator`
2. Place files in `Assets/Data/Datasets/{DatasetName}/PLY_WithMotion/`
3. In DatasetConfig Inspector, set **Processing Mode** to `PLY_WITH_MOTION`
4. Use `RuntimeMotionVectorVisualizer` component to visualize motion vectors

**Use Cases:**
- Scene flow analysis
- Motion vector visualization
- Optical flow research

#### Option 3: Binary Mode (CPU/GPU/ONESHADER)

**Requirements:**
- Raw sensor data files (`.rcst` for depth, `.rcsv` for color)
- Camera calibration files (`camera_extrinsics.yaml`, `camera_intrinsics.yaml`)
- Multi-camera setup with synchronized frames

**Setup Steps:**
1. Place binary sensor data on external drive or local path (e.g., `D:/Datasets/Totori/`)
2. Ensure folder structure contains `depth/camera_X/` and `color/camera_X/` directories
3. In DatasetConfig Inspector:
   - Set **Processing Mode** to `CPU`, `GPU`, or `ONESHADER`
   - Set **Binary Data Root Path** to the full external path (e.g., `D:/Datasets/Totori/`)
4. Ensure camera calibration files exist in `{BinaryDataRoot}/Config/`

**Processor Selection:**
- **CPU**: Single-threaded processing; good for debugging
- **GPU**: Multi-camera GPU fusion; recommended for real-time playback
- **ONESHADER**: Experimental unified shader approach

**Advantages:**
- Access to raw sensor data for custom processing
- Multi-camera synchronization and fusion
- Real-time undistortion and calibration

**Disadvantages:**
- Slower than PLY mode
- Requires external storage for large datasets
- More complex setup

### Setting Up BVH Skeletal Animation Data

#### Basic BVH Setup

1. **Place BVH file in dataset:**
   - Add your `.bvh` file to `Assets/Data/Datasets/{DatasetName}/BVH/`
   - File will be auto-detected (or manually assign in DatasetConfig)

2. **Enable BVH in DatasetConfig:**
   - Check **Enable BVH** checkbox
   - Optionally drag the BVH file into **BVH File** field (auto-detection is usually sufficient)

3. **Adjust BVH Transform:**
   - **Position Offset**: Align skeleton with point cloud origin
   - **Rotation Offset**: Correct skeleton orientation (common: `(0, 180, 0)` to flip)
   - **Scale**: Adjust skeleton size to match point cloud scale

#### Advanced: BVH Drift Correction

For long animations where the skeleton drifts from the point cloud over time:

1. **Create a BvhPlaybackCorrectionKeyframes asset:**
   - Right-click in Project window
   - Select `Create > Shiiba > BvhPlaybackCorrectionKeyframes`
   - Name it (e.g., `TotoriBVHAdjustData`)

2. **Configure keyframes:**
   - Use the custom inspector to add keyframes at specific timestamps
   - Set position/rotation offsets for each keyframe
   - The system interpolates between keyframes during playback

3. **Reference in DatasetConfig:**
   - Drag the `BvhPlaybackCorrectionKeyframes` asset into **BVH Drift Correction Data** field

4. **Fine-tune in Timeline:**
   - Play Timeline and observe skeleton alignment
   - Add/adjust keyframes as needed to minimize drift
   - Changes are applied in real-time during playback

### Integrating DatasetConfig with Timeline

1. **Add PointCloudPlayableAsset to Timeline:**
   - Open Timeline window
   - Create a new track or use existing Point Cloud track
   - Add `PointCloudPlayableAsset` clip to the track

2. **Assign DatasetConfig:**
   - Select the `PointCloudPlayableAsset` clip in Timeline
   - In Inspector, drag your `DatasetConfig` asset into the **Dataset Config** field

3. **Add BvhPlayableAsset (if using BVH):**
   - Create a BVH Animation track in Timeline
   - Add `BvhPlayableAsset` clip
   - The BVH file path is automatically retrieved from the same `DatasetConfig`

4. **Synchronize Timing:**
   - Align PointCloudPlayableAsset and BvhPlayableAsset clips to start at the same time
   - Adjust clip durations to match your dataset frame count
   - Use Timeline scrubbing to verify synchronization

### Validation and Troubleshooting

**Validate Configuration:**
```csharp
DatasetConfig config = // your config asset
bool isValid = config.ValidatePaths();
Debug.Log(config.GetSummary());
```

**Common Issues:**

| Problem | Solution |
|---------|----------|
| "BVH file not found" | Check `BVH/` folder exists and contains `.bvh` file, or manually assign BVH file in DatasetConfig |
| "PointCloud directory not found" | Verify Dataset Folder is correctly assigned in DatasetConfig |
| "No PLY files detected" | Check `PLY/` or `PLY_WithMotion/` folder exists with `frame_XXXX.ply` files |
| Skeleton misaligned with point cloud | Adjust BVH Position/Rotation Offset in DatasetConfig |
| Skeleton drifts over time | Create BvhPlaybackCorrectionKeyframes asset and add keyframes to correct drift |
| Binary mode not loading | Verify Binary Data Root Path points to valid external directory with depth/color folders |

**Dataset Switching:**
- Create multiple DatasetConfig assets for different datasets
- Switch datasets by assigning different DatasetConfig to Timeline PointCloudPlayableAsset
- All paths update automatically based on the selected config

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
| **BvhData** | Store BVH structure + frame data; apply frames to transforms | `GetFrame()`, `ApplyFrameToTransforms()` | BvhPlayableBehaviour, SceneFlowCalculator, offline processing |
| **BvhPlaybackFrameMapper** | Map Timeline time → BVH frame index (with drift correction) | `GetTargetFrameForTime()` | BvhPlayableBehaviour |
| **BvhDataReader** | Parse channel data from frame arrays (static utility) | `ReadChannelData()`, `GetRotationQuaternion()` | BvhData |
| **BvhPlaybackTransformCorrector** | Calculate drift-corrected root transform | `GetCorrectedRootPosition()`, `GetCorrectedRootRotation()` | BvhPlayableBehaviour |
| **BvhPlaybackCorrectionKeyframes** | Store keyframes; interpolate position/rotation at time | `GetPositionOffsetAtTime()`, `GetRotationOffsetAtTime()` | BvhPlaybackFrameMapper, BvhPlaybackTransformCorrector |
| **BvhJointHierarchyBuilder** | Create joint hierarchies from BVH data (static utility) | `CreateOrGetJointHierarchy()` | BvhPlayableBehaviour, SceneFlowCalculator |

**Key Design Principles:**

1. **BvhData handles frame application**: The `ApplyFrameToTransforms()` method is the authoritative implementation of frame-to-transform conversion, eliminating duplication across the codebase.

2. **BvhPlaybackFrameMapper is stateless and reusable**: All parameters are passed explicitly; no hidden dependencies. Can be instantiated fresh or cached without side effects.

3. **Timeline-independent utilities**: All core logic (BvhPlaybackFrameMapper, BvhPlaybackTransformCorrector, BvhDataReader, BvhJointHierarchyBuilder) work outside Timeline context, enabling reuse in Scene Flow, offline processing, and other components.

4. **Static utilities for shared operations**: BvhDataReader and BvhJointHierarchyBuilder are static utility classes providing reusable functionality across multiple consumers.

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
- [Assets/Script/bvh/BvhJointHierarchyBuilder.cs](Assets/Script/bvh/BvhJointHierarchyBuilder.cs) - Creates joint hierarchy (static utility class)
- [Assets/Script/bvh/BvhSkeletonVisualizer.cs](Assets/Script/bvh/BvhSkeletonVisualizer.cs) - Visualization logic
- [Assets/Script/bvh/BvhData.cs](Assets/Script/bvh/BvhData.cs) - Frame application via `ApplyFrameToTransforms()`

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
- **BvhDataReader.cs** - Static utility for parsing BVH motion data channels
- **BvhKeyframe.cs** - Represents individual animation keyframes
- **BvhJointHierarchyBuilder.cs** - Static utility for creating and managing joint hierarchies (frame-agnostic, idempotent)
- **BvhPlaybackCorrectionKeyframes.cs** - Corrects animation drift/misalignment issues via keyframe interpolation
- **BvhPlaybackFrameMapper.cs** - Maps Timeline time to BVH frame indices with drift correction
- **BvhPlaybackTransformCorrector.cs** - Calculates drift-corrected root transforms
- Custom inspector editor for drift correction parameters

This modular approach improves maintainability and allows fine-grained control over BVH animation playback.

#### Joint Hierarchy Builder (New - 2025-12-03)
**BvhJointHierarchyBuilder** is a new static utility class that extracts joint hierarchy creation logic from BvhPlayableBehaviour. Key features:
- **Reusable**: Any component can create joint hierarchies without Timeline dependency
- **Frame-agnostic**: Creates skeleton structure only; frame data applied separately via `BvhData.ApplyFrameToTransforms()`
- **Idempotent**: Safe to call multiple times without duplicating GameObjects
- **Usage**: `BvhJointHierarchyBuilder.CreateOrGetJointHierarchy(bvhData, parentTransform)`
- **Benefits**: Decouples hierarchy creation from Timeline lifecycle; enables use in SceneFlowCalculator and other components

### Scene Flow System (sceneflow/ directory)
New scene flow calculation system for optical flow/motion visualization:
- **SceneFlowCalculator.cs** - Core computation engine
- **SceneFlowBatchExporter.cs** - Batch export PLY files with embedded scene flow vectors
- **BoneSegmentData.cs** - Data structures for bone segmentation and motion tracking
- **SceneFlowCalculatorEditor.cs** - Custom editor UI for configuration and batch export
- Provides "Show Scene Flow" button for debugging point cloud motion

**Status:** Currently under development; bone segmentation component not yet fully functional.

### Enhanced Debugging Tools (utils/)
Added new debugging and visualization components:
- **BoundingVolumeDebugController.cs** - Runtime control of bounding volume display
- **CameraPositionGizmo.cs** - Visual indicators for camera positions
- **SetupStatusUI.cs** - Status display for project initialization
- **DebugImageExporter.cs** - Export debug images from runtime
- **TimelineUtil.cs** - Utility functions for Timeline operations

### Editor Tools (Assets/Script/Editor/)
Motion vector and point cloud testing tools:
- **MotionVectorPLYGenerator.cs** - Generate PLY files with embedded motion vectors
- **MotionVectorPLYValidator.cs** - Validate motion vector data in PLY files
- **PlyMotionVectorTest.cs** - Test and verify motion vector calculations

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

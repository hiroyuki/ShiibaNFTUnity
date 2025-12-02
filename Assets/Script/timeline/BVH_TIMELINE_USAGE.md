# BVH Timeline Integration - Usage Guide

## Overview
The BVH Timeline system allows you to import and play motion capture data from BVH files directly in Unity Timeline with full control over timing and transforms. The system uses automatic target discovery and DatasetConfig-based configuration for seamless integration.

## Core Components

### Timeline Integration
- **BvhPlayableAsset.cs** - Timeline clip asset for BVH data, auto-finds BVH file path from DatasetConfig
- **BvhPlayableBehaviour.cs** - Runtime playback controller with drift correction and frame mapping
- **TimelineController.cs** - Master timeline orchestration

### BVH Processing & Utilities
- **BvhFrameMapper.cs** - Maps Timeline time to BVH frame indices with keyframe interpolation support
- **BvhDriftCorrectionController.cs** - Calculates drift-corrected positions and rotations
- **BvhDataReader.cs** - Centralized channel data parsing (position/rotation extraction)
- **BvhFrameApplier.cs** - Abstract base for applying BVH frame data to transform hierarchies
- **BvhSkeletonVisualizer.cs** - Renders skeleton joints (spheres) and bones (lines) for debugging

### BVH Data Management
- **BvhData.cs** - Core BVH skeleton structure and frame storage
- **BvhImporter.cs** - BVH file parsing and import
- **BvhKeyframe.cs** - Keyframe data for Timeline-to-BVH-frame mapping
- **BvhDriftCorrectionData.cs** - Keyframe-based drift correction parameters
- **BvhPlayer.cs** - Standalone BVH playback controller
- **BvhDataCache.cs** - Caching layer for BVH data
- **BvhDataReader.cs** - Static utility for channel parsing

## Setup Instructions

### IMPORTANT: Use Your Existing Timeline GameObject

**You should use your existing GameObject that has the PlayableDirector for point clouds!**

Do NOT create a separate GameObject for BVH. A single Timeline can have multiple tracks:
- Your existing Point Cloud Track
- New BVH Track (added to the same timeline)

This allows perfect synchronization between point cloud and BVH motion data.

### 1. Create Root Transform for BVH Character

First, create an empty GameObject to serve as the root for your BVH skeleton:

1. In Hierarchy, right-click → **Create Empty**
2. Name it something like **"BVH_Character"** or **"MotionCapture_Root"**
3. Position it where you want the character to appear (e.g., at origin: 0, 0, 0)

**Why use an empty GameObject?**
- Clean hierarchy - BVH skeleton joints will be created as children automatically
- Easy positioning - Move/rotate this object to position the entire skeleton
- Multiple characters - Can have multiple root objects for different BVH files
- Non-destructive - Original setup remains unchanged

```
Scene Hierarchy:
  BVH_Character (empty GameObject) ← You'll bind this to the BVH Track
    └── (Skeleton joints auto-created: Hips, Spine, Head, etc.)
```

**Alternative:** If you already have a character with a skeleton hierarchy that matches BVH joint names, you can use that GameObject instead.

### 2. Add BVH Clip to Existing Timeline

1. Select your existing GameObject with the **PlayableDirector** component (the one used for point clouds)
2. Open the Timeline window (Window > Sequencing > Timeline)
3. Right-click in the tracks area and select **Add > BvhPlayableAsset** (or drag BvhPlayableAsset into timeline)
4. Select the clip and configure in the Inspector:

#### Target Auto-Discovery
- **Target GameObject Name** - Defaults to "BVH_Character" (auto-searches scene by name)
- Leave empty to use default, or change to match your GameObject name

#### Configuration Sources (in priority order)
BvhPlayableAsset automatically pulls settings from:
1. **DatasetConfig** (via MultiCameraPointCloudManager) - Primary source
2. **Override Transform Settings** - Check this box to use local Inspector values instead of DatasetConfig

#### Transform Settings (from DatasetConfig or local overrides)
- **Position Offset** - Offset applied to BVH_Character root (Vector3)
- **Rotation Offset** - Rotation offset in degrees for root joint (Vector3)
- **Scale** - Scale multiplier for all joint positions (Vector3)
- **Apply Root Motion** - If true, character moves based on BVH position data; if false, stays in place
- **Frame Offset** - Offset frames for sync with point cloud (default: 0)
- **Override Frame Rate** - Override BVH's frame rate (0 = use BVH's native rate)

### 3. Adjust Clip Timing

- **Drag clip edges** to adjust start/end time
- **Move clip** to change when motion starts
- **Clip duration** is automatically set from BVH file duration, but can be manually adjusted

---

## Quick Start Summary

1. **Create** empty GameObject named "BVH_Character" in Hierarchy
2. **Open** your existing Timeline (with point cloud clip)
3. **Add** BVH clip to the timeline
4. **Set** BVH file path in clip Inspector
5. **Verify** target GameObject name matches "BVH_Character"
6. **Adjust** scale/offset/rotation as needed
7. **Play** - skeleton auto-creates and animates!

## Transform Adjustment Features

### Position Offset
Use this to move the entire skeleton in world space:
```
positionOffset = new Vector3(0, 1, 0); // Move 1 unit up
```

### Rotation Offset
Apply a rotation offset to the root joint (in degrees):
```
rotationOffset = new Vector3(0, 90, 0); // Rotate 90° around Y axis
```

### Scale
Scale all position values uniformly or per-axis:
```
scale = new Vector3(0.01f, 0.01f, 0.01f); // Scale down to 1% (cm to m)
```

### Apply Root Motion
- **true** - Character will move based on BVH position data
- **false** - Character stays in place, only rotations are applied

## How It Works: Key Components

### BvhPlayableAsset (Timeline Clip)
- **Auto-discovers BVH file path** from DatasetConfig (via MultiCameraPointCloudManager)
- **Auto-finds target GameObject** by name in scene
- **Caches BVH data** to avoid reloading
- **Creates playable** with BvhPlayableBehaviour

### BvhPlayableBehaviour (Runtime Playback)
Timeline lifecycle:
- **OnGraphStart**: Creates joint hierarchy (GameObjects) from BvhData
- **PrepareFrame**: Called each frame to:
  - Map Timeline time → BVH frame index (via BvhFrameMapper)
  - Apply frame data to joints (via BvhFrameApplier)
  - Apply drift correction (via BvhDriftCorrectionController)
- **OnGraphStop**: Resets state

### BvhFrameMapper
- Maps Timeline time to BVH frame index
- Supports **keyframe-based mapping** via BvhDriftCorrectionData (enables speed adjustment)
- Falls back to linear mapping (frame = time * frameRate)
- Handles frame offsets and clamping

### BvhDriftCorrectionController
- Calculates drift-corrected position/rotation at any Timeline time
- Uses keyframe interpolation from BvhDriftCorrectionData
- Returns corrected position and rotation quaternion
- Works offline without Timeline

### BvhFrameApplier (Abstract Base)
- Recursive joint hierarchy application
- Reads channel data (position/rotation) via BvhDataReader
- Allows subclasses to customize adjustments (scale, offset, root motion)
- Used by BvhPlayableBehaviour.PlayableFrameApplier

### BvhSkeletonVisualizer
- Creates GameObject hierarchy: renderRoot → Joint spheres + Bone lines
- Updates visualization in LateUpdate
- Settings: skeletonColor, jointRadius, boneWidth
- Invoke delay: 1.0s (waits for joint hierarchy creation)

## Common Use Cases

### Case 1: Import BVH with correct scale
BVH files often use centimeters while Unity uses meters:
```
In DatasetConfig:
  BvhScale = (0.01, 0.01, 0.01)
```

### Case 2: Rotate character to face different direction
Character facing wrong way:
```
In DatasetConfig:
  BvhRotationOffset = (0, 180, 0)
```

### Case 3: Stationary animation
Only want the joint rotations, not position changes:
```
In DatasetConfig:
  BvhApplyRootMotion = false
```

### Case 4: Sync BVH with point cloud frames
Use frame offset to align BVH with point cloud:
```
In DatasetConfig:
  BvhFrameOffset = 5  // Start 5 frames ahead of timeline
```

### Case 5: Adjust BVH playback speed
Use BvhDriftCorrectionData keyframes to remap Timeline → BVH frames:
1. Create BvhDriftCorrectionData asset
2. Add keyframes: (timelineTime=0s, bvhFrame=0) → (timelineTime=10s, bvhFrame=200)
3. Assign to DatasetConfig.BvhDriftCorrectionData
4. Now 10s of Timeline plays 200 BVH frames (2x speed)

### Case 6: Multiple BVH clips in sequence
1. Add multiple BVH clips to the timeline
2. Arrange them sequentially
3. Each clip can target different BVH files via DatasetConfig
4. Timeline will play them in order with proper synchronization

## Timeline Control

The TimelineController provides keyboard controls:
- **Space** - Play/Pause
- **Escape** - Stop
- You can also use the Unity Timeline window controls

## Programmatic Control

```csharp
// Get reference to timeline
PlayableDirector timeline = GetComponent<PlayableDirector>();

// Play
timeline.Play();

// Pause
timeline.Pause();

// Seek to time
timeline.time = 5.0;

// Get BVH asset from timeline
TimelineAsset timelineAsset = timeline.playableAsset as TimelineAsset;
foreach (var track in timelineAsset.GetOutputTracks())
{
    foreach (var clip in track.GetClips())
    {
        BvhPlayableAsset bvhAsset = clip.asset as BvhPlayableAsset;
        if (bvhAsset != null)
        {
            // Access BVH data
            BvhData data = bvhAsset.GetBvhData();
            Debug.Log(data.GetSummary());

            // Get current BVH frame
            BvhPlayableBehaviour behaviour = bvhAsset.GetBvhPlayableBehaviour();
            int currentFrame = behaviour.GetCurrentFrame();

            // Get drift correction data
            BvhDriftCorrectionData driftData = bvhAsset.GetDriftCorrectionData();

            // Get BVH character position (for keyframe recording)
            Vector3 characterPos = bvhAsset.GetBvhCharacterPosition();

            // Reload BVH file
            bvhAsset.ReloadBvhData();
        }
    }
}
```

### Using BVH Frame Mapper Directly
```csharp
// Map timeline time to BVH frame without Timeline
BvhFrameMapper mapper = new BvhFrameMapper();
BvhData bvhData = BvhImporter.ImportFromBVH("path/to/file.bvh");

// Get frame for timeline time 2.5 seconds
int targetFrame = mapper.GetTargetFrameForTime(
    timelineTime: 2.5f,
    bvhData: bvhData,
    driftCorrectionData: null,  // Optional
    frameOffset: 0
);

Debug.Log($"Timeline 2.5s = BVH frame {targetFrame}");
```

### Using Drift Correction Controller Directly
```csharp
// Calculate corrected position/rotation without Timeline
BvhDriftCorrectionController driftController = new BvhDriftCorrectionController();
BvhDriftCorrectionData driftData = /* load from DatasetConfig */;

// Get corrected transforms for timeline time 5.0 seconds
Vector3 correctedPos = driftController.GetCorrectedRootPosition(
    timelineTime: 5.0f,
    driftCorrectionData: driftData,
    positionOffset: Vector3.zero
);

Quaternion correctedRot = driftController.GetCorrectedRootRotation(
    timelineTime: 5.0f,
    driftCorrectionData: driftData,
    rotationOffset: Vector3.zero
);
```

## Automatic Skeleton Creation

BvhPlayableBehaviour.CreateJointHierarchy() (called in OnGraphStart):
- Creates GameObjects for each BVH joint if not found
- Builds parent-child transform hierarchy matching BVH structure
- Sets localPosition to joint.Offset, localRotation to identity
- Skips end sites (leaf nodes without animation)

Can pre-create hierarchy for better control:
- Manually create GameObjects matching BVH joint names
- System will find and reuse them instead of creating new ones
- Useful if you want custom components or materials on joints

## Configuration via DatasetConfig

Key BVH fields in DatasetConfig:
```csharp
public string BvhFilePath { get; set; }
public Vector3 BvhPositionOffset { get; set; }
public Vector3 BvhRotationOffset { get; set; }
public Vector3 BvhScale { get; set; }
public bool BvhApplyRootMotion { get; set; }
public float BvhOverrideFrameRate { get; set; }  // 0 = use BVH's rate
public int BvhFrameOffset { get; set; }
public BvhDriftCorrectionData BvhDriftCorrectionData { get; set; }
```

These are loaded by BvhPlayableAsset at clip creation time and passed to BvhPlayableBehaviour.

## Troubleshooting

### "BVH data not loaded"
- Verify BvhFilePath is set in DatasetConfig
- Check that the file path is correct (absolute or relative to project root)
- Confirm MultiCameraPointCloudManager is in scene with DatasetConfig assigned
- Check Console for load errors

### "Target GameObject not found"
- Ensure a GameObject named "BVH_Character" exists in scene, OR
- Set targetGameObjectName in BvhPlayableAsset Inspector to match your GameObject

### Skeleton not visible
- Check BvhSkeletonVisualizer is attached to BVH_Character
- Wait 1+ second for OnGraphStart to complete and visualization to create
- Verify jointRadius and boneWidth are > 0
- Check if BVH_Visuals GameObject was created (child of BVH_Character)

### Motion doesn't match point cloud
- Adjust BvhFrameOffset in DatasetConfig to sync frames
- Check BvhScale matches point cloud scale
- Use BvhDriftCorrectionData keyframes to remap Timeline → BVH frames
- Verify frame rates: (timeline duration * timeline frame rate) ≈ (BVH frame count / BVH frame rate)

### Motion looks wrong (scale, rotation, position)
- Adjust BvhScale if BVH uses different units (typically 0.01 for cm→m)
- Adjust BvhRotationOffset if character faces wrong direction
- Toggle BvhApplyRootMotion if character should stay in place
- Adjust BvhPositionOffset to reposition character

### Multiple clips overlapping cause issues
- Only one BVH clip should be active at a time on the same target
- Arrange clips sequentially rather than overlapping
- Timeline automatically switches between clips

## Editor Helper Functions

### BvhPlayableAsset Context Menu
Right-click on BvhPlayableAsset in Inspector:
- **Update Clip Duration from BVH** - Logs BVH duration and frame count
  - Allows manual clip duration adjustment based on loaded BVH data

### DatasetConfig Integration
- **MultiCameraPointCloudManager** finds and stores DatasetConfig reference
- **BvhPlayableAsset** reads BVH settings from DatasetConfig at clip creation
- **BvhDriftCorrectionDataEditor** (custom inspector) provides UI for keyframe editing

## Integration with Point Cloud Timeline

### Single Timeline, Multiple Clips

**IMPORTANT:** Use ONE GameObject with ONE PlayableDirector for both point cloud and BVH clips.

#### Setup:
```
Scene Hierarchy:
  GameObject (e.g., "TimelineController")
    ├── Component: PlayableDirector
    │   └── Timeline Asset:
    │       ├── Point Cloud Clip (auto-finds MultiCameraPointCloudManager)
    │       └── BVH Clip (auto-finds "BVH_Character" GameObject)
    └── Component: TimelineController (optional, for keyboard controls)
```

#### Benefits:
- **Perfect Synchronization**: Both clips play at the same timeline position
- **Single Control**: One play/pause/stop button controls both
- **Visual Timeline**: See both point cloud and BVH timing in one view
- **Easy Adjustment**: Drag clips to align BVH motion with point cloud frames

#### Auto-Find Targets:
- **Point Cloud Clip** finds `MultiCameraPointCloudManager` in scene automatically
- **BVH Clip** finds GameObject named "BVH_Character" in scene automatically

Both systems auto-find their targets - no manual binding needed.

### Example Workflow:

1. You already have point cloud timeline working
2. Create "BVH_Character" GameObject in scene
3. Add BVH clip to the SAME timeline
4. Set file path and adjustments in BVH clip inspector
5. Press Play - both point cloud and character motion play together!

### Synchronization Tips:

- Both clips share the same timeline time via PlayableDirector
- Frame sync via BvhFrameOffset in DatasetConfig
- Use BvhDriftCorrectionData keyframes for fine-grained Timeline → BVH frame mapping
- Playback speed: adjust timeline duration to change playback speed (timeline duration controls rate)
- Can have multiple BVH clips playing different characters (each needs its own GameObject/target)

### Data Flow

```
Timeline (PlayableDirector)
  ├── PointCloudPlayableAsset/Behaviour
  │   └── Finds MultiCameraPointCloudManager → DatasetConfig
  │       └── Reads point cloud settings
  │
  └── BvhPlayableAsset
      └── Reads BvhFilePath, scale, offset, frameOffset from DatasetConfig
          └── Creates BvhPlayableBehaviour
              └── Each frame (PrepareFrame):
                  1. BvhFrameMapper: Timeline time + frameOffset → BVH frame index
                  2. BvhFrameApplier: Apply BVH frame data to joints
                  3. BvhDriftCorrectionController: Apply drift correction
                  4. Set BVH_Character position/rotation
```

## Advanced: Frame Mapper & Drift Correction

### Linear Frame Mapping (Default)
```
BVH Frame = floor(Timeline Time * BVH Frame Rate)
```

Example: 2.5s timeline @ 30fps BVH = frame 75

### Keyframe-Based Mapping (with BvhDriftCorrectionData)
Keyframes define Timeline → BVH frame mapping:
- Creates custom speed curves by defining frame numbers at specific timeline times
- Interpolates between keyframes
- Allows variable playback speeds

Example keyframes:
```
Keyframe 1: timelineTime=0.0s, bvhFrame=0
Keyframe 2: timelineTime=5.0s, bvhFrame=200    // 200 frames in 5s = 40fps
Keyframe 3: timelineTime=10.0s, bvhFrame=300   // 100 frames in 5s = 20fps
```

At timeline time 2.5s: interpolates between kf1 and kf2 → frame ≈ 100

### Drift Correction
Corrects root position/rotation drift over time:
- Each keyframe in BvhDriftCorrectionData contains:
  - `timelineTime`: When to apply correction
  - `anchorPositionCorrection`: Position offset (e.g., to shift character back)
  - `anchorRotationCorrection`: Rotation offset (e.g., to straighten character)
- Linear interpolation between keyframes
- Applied in PrepareFrame via BvhDriftCorrectionController

Example: If BVH drifts forward over 10 seconds:
```
Keyframe 1: time=0s, anchorPos=(0,0,0), anchorRot=(0,0,0)
Keyframe 2: time=10s, anchorPos=(0,0,-2), anchorRot=(0,5,0)  // Shift back 2 units, rotate 5°
```

At time 5s: anchorPos interpolates to (0,0,-1), rotation to (0,2.5,0)

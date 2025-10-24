# BVH Timeline Integration - Usage Guide

## Overview
The BVH Timeline system allows you to import and play motion capture data from BVH files directly in Unity Timeline with full control over timing and transforms.

## Files Created
- **BvhPlayableAsset.cs** - Timeline clip asset for BVH data
- **BvhPlayableBehaviour.cs** - Runtime behaviour for BVH playback
- **TimelineController.cs** - Updated to support BVH clips

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

#### BVH File Settings
- **Bvh File Path** - Absolute or relative path to your .bvh file
- **Auto Load Bvh** - Load the file when timeline starts (recommended: true)

#### Target
- **Target GameObject Name** - Name of GameObject in scene to apply motion to (default: "BVH_Character")

#### Transform Adjustments
- **Position Offset** - Offset applied to root motion (Vector3)
- **Rotation Offset** - Rotation offset in degrees (Vector3)
- **Scale** - Scale multiplier for all positions (Vector3)
- **Apply Root Motion** - If false, only rotation is applied, not position

#### Playback
- **Override Frame Rate** - Override BVH's frame rate (0 = use BVH's rate)

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

## Common Use Cases

### Case 1: Import BVH with correct scale
BVH files often use centimeters while Unity uses meters:
```
Scale = (0.01, 0.01, 0.01)
```

### Case 2: Rotate character to face different direction
Character facing wrong way:
```
Rotation Offset = (0, 180, 0)
```

### Case 3: Stationary animation
Only want the joint rotations, not position changes:
```
Apply Root Motion = false
```

### Case 4: Multiple BVH clips in sequence
1. Add multiple BVH clips to the timeline
2. Arrange them sequentially
3. Timeline will play them in order

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

            // Reload BVH file
            bvhAsset.ReloadBvhData();
        }
    }
}
```

## Automatic Skeleton Creation

If the target Transform doesn't have matching child transforms for the BVH joints:
- The system will automatically create GameObjects for each joint
- Joints are created with proper offsets and hierarchy
- You can pre-create the skeleton for better control

## Troubleshooting

### "BVH data not loaded"
- Check that the file path is correct
- Use absolute path or path relative to project root
- Ensure autoLoadBvh is checked

### "Target GameObject not found"
- Ensure a GameObject named "BVH_Character" exists in the scene
- Or set targetGameObjectName in the clip inspector to match your GameObject name

### Motion looks wrong
- Adjust Scale if BVH uses different units
- Try different Rotation Offset values
- Toggle Apply Root Motion

### Multiple clips overlapping
- Only one BVH clip should be active at a time
- Arrange clips sequentially rather than overlapping

## Editor Helper Functions

Right-click on BvhPlayableAsset in Inspector:
- **Update Clip Duration from BVH** - Resize clip to match BVH file duration

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

- Both clips share the same timeline time
- If your BVH is 30fps and point cloud is 30fps, they stay in sync
- Use clip offsets to adjust relative timing
- Can have multiple BVH clips playing different characters (each needs its own GameObject)

# Timeline Navigation and Frame Seeking - Documentation Index

This documentation provides a complete analysis of how timeline navigation and frame seeking work in ShiibaNFTUnity, covering arrow key input handling, PlayableDirector time updates, and how PointCloudPlayableBehaviour and BvhPlayableBehaviour respond to timeline seeking.

## Quick Answer: Where is Left Arrow Key Handled?

**Location:** NOT in TimelineController.cs

**Actually located in:**
- `Assets/Script/pointcloud/handler/PlyModeHandler.cs` (Lines 77-90)
  - Fully implements left arrow support
  - `leftArrowKey.wasPressedThisFrame` -> `LoadPreviousPlyFrame()`
  
- `Assets/Script/pointcloud/handler/BinaryModeHandler.cs` (Lines 256-269)
  - Does NOT implement left arrow
  - Logs warning: "Backward navigation not supported. Use timeline controls."

**TimelineController.cs (Lines 78-107):**
- Handles: Space (Play/Pause), Escape (Stop), 0 (Reset), Shift+A (Add Keyframe)
- Does NOT handle arrow keys
- Arrow keys are handled at the processing mode handler level

---

## Document Organization

### 1. TIMELINE_SEEKING_ANALYSIS.md
**Comprehensive 9-section analysis covering:**

1. Arrow Key Input Handling
   - PlyModeHandler implementation (left and right)
   - BinaryModeHandler implementation (right only)
   - Key behavioral differences

2. PlayableDirector.time Property Updates
   - TimelineController control methods
   - Supported input controls (Space, Escape, 0, Shift+A)
   - No arrow key handling in TimelineController

3. PointCloudPlayableBehaviour Response to Timeline Seeking
   - Full PrepareFrame() implementation
   - Frame rate dependency (fixed 30fps)
   - Time to frame conversion formula
   - Update optimization (frame change detection)

4. BvhPlayableBehaviour Response to Timeline Seeking
   - Full PrepareFrame() implementation
   - Frame offset support for synchronization
   - Drift correction application
   - Key differences from PointCloud

5. Comparison: Frame-by-Frame vs Continuous Playback
   - Arrow key characteristics
   - Timeline playback characteristics
   - When each is used

6. Architecture Flow Diagram
   - Two independent input paths
   - Call chains for both seeking methods

7. Key Implementation Differences
   - Feature comparison table
   - What makes BVH different from PointCloud

8. Synchronization Mechanisms
   - Timeline-based sync (PointCloud + BVH)
   - Arrow key seeking (Point Cloud only)
   - How systems keep in sync (or don't)

9. Implications for Frame-Accurate Navigation
   - Known issues and limitations
   - Solution approaches

**Use this document for:**
- Deep dive into implementation details
- Understanding synchronization mechanisms
- Learning about known issues
- Reference for architecture decisions

---

### 2. TIMELINE_COMPARISON.md
**Side-by-side code comparisons and call chains:**

- PrepareFrame() implementation comparison
- OnGraphStart() initialization comparison
- Arrow key handling in both modes
- Frame calculation formulas
- Drift correction pipeline
- Synchronization strategies
- Configuration points
- Error handling approaches
- Complete call chain diagrams

**Use this document for:**
- Comparing PointCloud and BVH implementations
- Understanding call chains
- Learning configuration options
- Visual flow diagrams
- Code implementation details

---

## Quick Reference: Input Controls

| Input | Handler | Effect |
|-------|---------|--------|
| **Left Arrow** | PlyModeHandler | Load previous PLY frame |
| **Left Arrow** | BinaryModeHandler | WARNING - Not supported |
| **Right Arrow** | PlyModeHandler | Load next PLY frame |
| **Right Arrow** | BinaryModeHandler | Navigate next synchronized frame |
| **Space** | TimelineController | Play/Pause |
| **Escape** | TimelineController | Stop |
| **0 (Digit0)** | TimelineController | Reset to beginning |
| **Shift+A** | TimelineController | Add drift correction keyframe |

---

## Key Architecture Findings

### Two Independent Seeking Paths

#### Path 1: Arrow Keys (Point Cloud Only)
```
Left/Right Arrow
    ↓
Handler.HandleNavigation()
    ↓
Handler.LoadPlyFrame(frameIndex) / NavigateToNextFrame()
    ↓
Point cloud updates immediately
[Timeline.time NOT updated]
[BVH NOT synchronized]
```

#### Path 2: Timeline Playback (Point Cloud + BVH)
```
PlayableDirector.time = X
    ↓
Timeline calls PrepareFrame() on all Playables
    ↓
PointCloudPlayableBehaviour.PrepareFrame()
    frame = floor(time * 30)
    MultiCameraPointCloudManager.SeekToFrame(frame)
    ↓
BvhPlayableBehaviour.PrepareFrame()
    frame = floor(time * bvhFrameRate) + frameOffset
    ApplyFrame(frame)
    ApplyDriftCorrection(time)
```

---

## Critical Code Locations

| Component | File | Key Lines | Purpose |
|-----------|------|-----------|---------|
| Timeline Controls | TimelineController.cs | 78-107 | Space/Escape/0/Shift+A handling |
| Time Setting | TimelineController.cs | 160-171 | ResetTimeline() with Evaluate() |
| Point Cloud Sync | PointCloudPlayableBehaviour.cs | 35-47 | PrepareFrame() method |
| BVH Sync | BvhPlayableBehaviour.cs | 51-77 | PrepareFrame() method |
| BVH Init | BvhPlayableBehaviour.cs | 27-43 | OnGraphStart() skeleton creation |
| Drift Correction | BvhPlayableBehaviour.cs | 83-114 | ApplyDriftCorrection() method |
| PLY Arrow Keys | PlyModeHandler.cs | 77-90 | Both left and right arrows |
| Binary Arrow Keys | BinaryModeHandler.cs | 256-269 | Right arrow only |

---

## Key Differences: PointCloud vs BVH

| Feature | PointCloud | BVH |
|---------|-----------|-----|
| **Frame Rate Source** | Fixed property (30fps) | Dynamic (BvhData.FrameRate) |
| **Frame Offset** | None | frameOffset (for sync) |
| **OnGraphStart** | None (intentional) | Creates joint hierarchy |
| **Drift Correction** | None | Applied every frame |
| **Arrow Key Support** | Yes (PLY), No (Binary) | No (Timeline only) |
| **Frame Update** | Reactive (change only) | Reactive (change only) |
| **Bounds Checking** | Handler delegates | Explicit [0, FrameCount-1] |
| **OnGraphStop** | None | Reset currentFrame = -1 |

---

## Known Limitations and Issues

### 1. Binary Mode Missing Backward Seeking
- **File:** BinaryModeHandler.cs (Line 265-268)
- **Issue:** Left arrow not implemented
- **Reason:** Expensive to reconstruct backward from raw binary data
- **Workaround:** Use Timeline UI scrubbing
- **Code:** `Debug.LogWarning("Backward navigation not supported. Use timeline controls.");`

### 2. Arrow Keys Cause Desynchronization
- **Issue:** Using arrow keys desynchronizes point cloud and BVH
- **Cause:** Arrow keys only update point cloud, not Timeline
- **Effect:** Visual sync lost between point cloud and skeleton
- **Solution:** Use Timeline to resync both systems

### 3. Frame Rate Mismatch
- **Issue:** PointCloud uses fixed 30fps, BVH uses file's frame rate
- **Potential:** Desync at different playback speeds
- **Solution:** Use frameOffset property on BvhPlayableBehaviour
- **Configuration:** Set in Inspector on BVH clip

---

## How Timeline Seeking Works

### Step 1: User Input
```
User drags Timeline or presses arrow key
```

### Step 2: Handler Response
```
Arrow Key Path:
  PlyModeHandler/BinaryModeHandler.HandleNavigation()
  
Timeline Path:
  PlayableDirector.time = X
  timeline.Evaluate() (forces update)
```

### Step 3: Playable Response
```
Timeline system calls PrepareFrame() on all Playables

PointCloudPlayableBehaviour.PrepareFrame():
  1. Read: currentTime = playable.GetTime()
  2. Convert: targetFrame = floor(currentTime * 30)
  3. Update: pointCloudManager.SeekToFrame(targetFrame)

BvhPlayableBehaviour.PrepareFrame():
  1. Read: currentTime = playable.GetTime()
  2. Convert: targetFrame = floor(currentTime * bvhFrameRate) + frameOffset
  3. Clamp: targetFrame = clamp(targetFrame, 0, FrameCount-1)
  4. Apply: ApplyFrame(targetFrame)
  5. Correct: ApplyDriftCorrection(currentTime)
```

### Step 4: Visual Update
```
Point cloud mesh updates
Skeleton joints update
Camera view reflects changes
```

---

## Frame Rate Formulas

### PointCloud Frame Calculation
```csharp
targetFrame = Mathf.FloorToInt((float)(currentTime * 30.0f))
```
- Simple linear conversion
- No offset
- No bounds checking

### BVH Frame Calculation
```csharp
targetFrame = Mathf.FloorToInt((float)(currentTime * bvhData.FrameRate));
targetFrame += frameOffset;  // Synchronization adjustment
targetFrame = Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1);
```
- Dynamic frame rate
- Supports synchronization offset
- Explicit bounds checking

---

## Configuration Points

All settings can be configured in the Inspector on the Playable clips:

**PointCloudPlayableBehaviour:**
- `frameRate` - Playback frame rate (default 30)

**BvhPlayableBehaviour:**
- `frameOffset` - Synchronization offset with point cloud
- `positionOffset` - Initial BVH_Character position (auto-set)
- `rotationOffset` - Root joint rotation adjustment
- `scale` - Animation scale factor
- `applyRootMotion` - Use root joint animation
- `driftCorrectionData` - Reference to drift correction keyframes

---

## Understanding Drift Correction

Drift correction is applied **every frame** in BvhPlayableBehaviour.PrepareFrame():

```csharp
private void ApplyDriftCorrection(float timelineTime)
{
    // Get interpolated target position from keyframes
    Vector3 targetAnchorPositionRelative = 
        driftCorrectionData.GetAnchorPositionAtTime(timelineTime);
    
    // Apply correction: baseline (initial position) + interpolated adjustment
    targetTransform.localPosition = positionOffset + targetAnchorPositionRelative;
}
```

**Key Points:**
- `positionOffset` = Initial BVH_Character position (saved at OnGraphStart)
- Keyframe interpolation provides smooth correction over time
- Applied independently of frame changes
- Enables continuous position adjustment

---

## When to Use Each Method

### Use Arrow Keys (PLY Mode Only)
- Quick frame-by-frame review
- Doesn't need BVH synchronized
- Forward-only navigation (left arrow not supported in Binary)
- Point cloud only visibility needed

### Use Timeline
- Synchronized playback of point cloud and BVH
- Precise timing control
- Backward seeking support
- Both visuals needed together

### Use Timeline UI Scrubbing
- Continuous frame scrubbing
- Variable speed playback
- Exact temporal positioning
- Preferred for general use

---

## File References (Absolute Paths)

```
/Volumes/horristicSSD2T/repos/ShiibaNFTUnity/
  Assets/Script/timeline/
    TimelineController.cs
    PointCloudPlayableBehaviour.cs
    BvhPlayableBehaviour.cs
  Assets/Script/pointcloud/
    MultiCamPointCloudManager.cs
    handler/
      PlyModeHandler.cs
      BinaryModeHandler.cs
```

---

## Related Documentation

- **CLAUDE.md** - Project overview and architecture
- **TIMELINE_SEEKING_ANALYSIS.md** - Complete detailed analysis
- **TIMELINE_COMPARISON.md** - Side-by-side code comparisons
- **BvhDriftCorrectionData.cs** - Drift correction keyframe system
- **README.md** - General project documentation

---

## Questions Answered

### Where is the left arrow key handled?
- **PlyModeHandler.cs, Lines 77-90** - Fully implemented
- **BinaryModeHandler.cs, Lines 256-269** - Not implemented (warning logged)

### How does PointCloudPlayableBehaviour respond to timeline seeking?
- Reads `playable.GetTime()` in PrepareFrame()
- Converts time to frame: `floor(time * 30)`
- Calls `pointCloudManager.SeekToFrame(targetFrame)`

### How does BvhPlayableBehaviour respond to timeline seeking?
- Reads `playable.GetTime()` in PrepareFrame()
- Converts time to frame: `floor(time * bvhFrameRate) + frameOffset`
- Clamps to valid range
- Applies frame data to joints
- Applies drift correction

### How is PlayableDirector.time updated?
- TimelineController.time = X (direct property set)
- timeline.Evaluate() (forces immediate update)
- Used in ResetTimeline() method

### What are the differences between frame-by-frame and continuous playback?
- **Frame-by-frame (arrows):** Direct frame loading, no timeline sync, point cloud only
- **Continuous (timeline):** Time-based playback, both systems synced, smoother playback

---

## Summary

ShiibaNFTUnity implements two independent frame seeking mechanisms:

1. **Arrow Key Seeking** - Direct frame stepping in handlers, bypasses Timeline
   - Supported in PLY mode (both directions)
   - Limited in Binary mode (forward only)
   - Point cloud only (BVH not affected)

2. **Timeline Seeking** - Time-based playback through PlayableDirector
   - Both point cloud and BVH synchronized
   - Controlled via Timeline UI or keyboard shortcuts
   - Supports drift correction for BVH

Both systems use different frame rate sources (PointCloud: fixed 30fps, BVH: dynamic), requiring the `frameOffset` property for precise synchronization.

For questions or clarifications, refer to the detailed documents:
- TIMELINE_SEEKING_ANALYSIS.md for complete analysis
- TIMELINE_COMPARISON.md for code comparisons

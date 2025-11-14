# Timeline Navigation and Frame Seeking Analysis

## Summary

The timeline seeking system in ShiibaNFTUnity uses **independent seeking paths**:
1. **PlayableDirector.time** - controls overall timeline position (set by Timeline UI/scrubbing)
2. **PrepareFrame() callbacks** - sync point clouds and BVH to timeline time
3. **Arrow keys** - direct frame stepping in point cloud handlers (bypasses Timeline)

---

## 1. Arrow Key Input Handling

### Location 1: PlyModeHandler (Lines 77-90)
**File:** `Assets/Script/pointcloud/handler/PlyModeHandler.cs`

```csharp
private void HandlePlyModeNavigation()
{
    if (Keyboard.current == null) return;

    if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
    {
        LoadNextPlyFrame();
    }

    if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
    {
        LoadPreviousPlyFrame();
    }
}
```

**Behavior:**
- **Right Arrow:** Loads next frame
- **Left Arrow:** Loads previous frame
- Calls `LoadPlyFrame(frameIndex)` which directly loads PLY file without updating Timeline

### Location 2: BinaryModeHandler (Lines 256-269)
**File:** `Assets/Script/pointcloud/handler/BinaryModeHandler.cs`

```csharp
private void HandleSynchronizedFrameNavigation()
{
    if (Keyboard.current == null) return;

    if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
    {
        NavigateToNextSynchronizedFrame();
    }

    if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
    {
        Debug.LogWarning("Backward navigation not supported. Use timeline controls.");
    }
}
```

**Behavior:**
- **Right Arrow:** Navigates forward (multi-camera synchronized)
- **Left Arrow:** **NOT IMPLEMENTED** - logs warning, directs users to use Timeline controls

**KEY DIFFERENCE:** PlyModeHandler supports left arrow (backward), BinaryModeHandler does not.

### Location 3: TimelineController (Lines 68-80)
**File:** `Assets/Script/timeline/TimelineController.cs`

```csharp
// Shift+A: Add drift correction keyframe
if (Keyboard.current[addKeyframeKey].wasPressedThisFrame &&
    (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
{
    AddDriftCorrectionKeyframe();
}

// Shift+U: Update current keyframe
if (Keyboard.current[updateKeyframeKey].wasPressedThisFrame &&
    (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
{
    UpdateCurrentDriftCorrectionKeyframe();
}
```

**Behavior:**
- **Shift+A:** Add a new drift correction keyframe at the current timeline time
- **Shift+U:** Update the last edited keyframe with current BVH position

---

## 2. PlayableDirector.time Property Updates

### TimelineController (Lines 52-81)
**File:** `Assets/Script/timeline/TimelineController.cs`

```csharp
void HandleInput()
{
    if (Keyboard.current == null) return;

    // Space key: Play/Pause toggle
    if (Keyboard.current[playPauseKey].wasPressedThisFrame)
    {
        TogglePlayPause();
    }

    // Escape key: Stop
    if (Keyboard.current[stopKey].wasPressedThisFrame)
    {
        StopTimeline();
    }

    // Shift+A: Add drift correction keyframe
    if (Keyboard.current[addKeyframeKey].wasPressedThisFrame &&
        (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
    {
        AddDriftCorrectionKeyframe();
    }

    // Shift+U: Update current keyframe
    if (Keyboard.current[updateKeyframeKey].wasPressedThisFrame &&
        (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
    {
        UpdateCurrentDriftCorrectionKeyframe();
    }
}
```

**Supported Controls:**
- **Space:** Play/Pause
- **Escape:** Stop
- **Shift+A:** Add drift correction keyframe at current timeline position
- **Shift+U:** Update the last edited keyframe with current BVH position
- **NO LEFT/RIGHT ARROW KEYS** in TimelineController

**NOTE:** Arrow keys are NOT handled in TimelineController. Timeline seeking is done through:
- Timeline UI scrubbing (manual drag)
- PlayableDirector.time programmatic set
- Continuous playback via Play()

---

## 3. PointCloudPlayableBehaviour Response to Timeline Seeking

### File: `Assets/Script/timeline/PointCloudPlayableBehaviour.cs`

```csharp
public class PointCloudPlayableBehaviour : PlayableBehaviour
{
    public float frameRate = 30f;
    public MultiCameraPointCloudManager pointCloudManager;
    
    private int currentFrame = -1;
    
    public override void OnGraphStart(Playable playable)
    {
        // Don't auto-reset to first frame - let user control timeline position
    }

    public override void PrepareFrame(Playable playable, FrameData info)
    {
        if (pointCloudManager == null) return;
        
        double currentTime = playable.GetTime();
        int targetFrame = Mathf.FloorToInt((float)(currentTime * frameRate));
        
        if (targetFrame != currentFrame)
        {
            pointCloudManager.SeekToFrame(targetFrame);
            currentFrame = targetFrame;
        }
    }
}
```

**Seeking Mechanism:**

| Aspect | Behavior |
|--------|----------|
| **Read Time From** | `playable.GetTime()` - Timeline playable's current time |
| **Time to Frame Conversion** | `targetFrame = floor(currentTime * frameRate)` |
| **Update Condition** | Only if `targetFrame != currentFrame` (optimization) |
| **SeekToFrame Call** | `pointCloudManager.SeekToFrame(targetFrame)` |
| **OnGraphStart** | Does NOT auto-reset (user controls timeline position) |
| **Response Type** | **Reactive** - updates only when timeline time changes |

**Frame Rate Dependency:**
- Uses `frameRate` property (default 30f)
- Frame = floor(time_in_seconds * frameRate)
- Example: At 2.0 seconds with 30fps → frame = floor(2.0 * 30) = 60

---

## 4. BvhPlayableBehaviour Response to Timeline Seeking

### File: `Assets/Script/timeline/BvhPlayableBehaviour.cs`

Key excerpts showing timeline seeking response:

```csharp
public override void PrepareFrame(Playable playable, FrameData info)
{
    if (bvhData == null || targetTransform == null) return;

    // Get current time from timeline
    double currentTime = playable.GetTime();

    // Calculate frame based on BVH's frame rate
    float bvhFrameRate = bvhData.FrameRate;
    int targetFrame = Mathf.FloorToInt((float)(currentTime * bvhFrameRate));

    // Apply frame offset for synchronization with point cloud
    targetFrame += frameOffset;

    // Clamp to valid range
    targetFrame = Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1);

    // Only update if frame changed
    if (targetFrame != currentFrame)
    {
        ApplyFrame(targetFrame);
        currentFrame = targetFrame;
    }

    // Apply drift correction if enabled
    ApplyDriftCorrection((float)currentTime);
}
```

**Seeking Mechanism:**

| Aspect | Behavior |
|--------|----------|
| **Read Time From** | `playable.GetTime()` - Timeline playable's current time |
| **Time to Frame Conversion** | `targetFrame = floor(currentTime * BvhFrameRate)` |
| **Frame Offset Application** | `targetFrame += frameOffset` (for sync with point cloud) |
| **Clamping** | Clamps to [0, FrameCount-1] |
| **Update Condition** | Only if `targetFrame != currentFrame` |
| **ApplyFrame Call** | Applies BVH frame data to joint hierarchy |
| **Drift Correction** | Applied every frame to root transform position |
| **OnGraphStart** | **Creates joint hierarchy** with `CreateJointHierarchy()` |
| **Response Type** | **Reactive** - updates when timeline time changes |

**Key Differences from PointCloudPlayableBehaviour:**

1. **Frame Offset:** Supports `frameOffset` property for alignment with point cloud (PointCloud doesn't)
2. **OnGraphStart:** Creates joint hierarchy (PointCloud doesn't auto-reset)
3. **Drift Correction:** Applied every frame using keyframe interpolation
4. **Frame Rate:** Reads from BVH data (`bvhData.FrameRate`), not a fixed property

---

## 5. Comparison: Frame-by-Frame vs Continuous Playback

### Frame-by-Frame Seeking (Arrow Keys)

**PLY Mode (with Left Arrow support):**
```csharp
if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
{
    LoadPreviousPlyFrame();  // Direct frame load
}
```

**Binary Mode (no Left Arrow):**
```csharp
if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
{
    Debug.LogWarning("Backward navigation not supported. Use timeline controls.");
}
```

**Characteristics:**
- Bypasses Timeline completely
- Direct seek to specific frame index
- Updates internal frame counter
- Does NOT sync Timeline.time
- Isolated to point cloud handler (BVH not affected)

### Continuous Playback (Timeline)

**Via PlayableDirector:**
```csharp
timeline.Play();        // Start continuous playback
```

**PrepareFrame Processing:**
- Called every frame by Timeline system
- Converts `PlayableDirector.time` to frame index
- Synchronizes BOTH PointCloud and BVH
- PointCloud and BVH use same timeline position

**Characteristics:**
- Timeline drives both point cloud and BVH
- Time-based seeking (continuous)
- Both systems read from same `playable.GetTime()`
- Synchronized across both visuals

---

## 6. Architecture Flow Diagram

```
INPUT PATHS:
============

Path 1: Arrow Keys (Point Cloud Only)
────────────────────────────────────
Right/Left Arrow
    ↓
PlyModeHandler.HandlePlyModeNavigation()
    ↓
LoadPlyFrame(frameIndex)
    ↓
MultiPointCloudView.LoadFromPLY()
[No Timeline update, BVH unaffected]


Path 2: Timeline Scrubbing/Playback (Both Systems)
─────────────────────────────────────────────────
User: Play/Pause/Scrub Timeline
    ↓
PlayableDirector.time = X
    ↓
Timeline calls PrepareFrame() on all Playables
    ↓
┌─ PointCloudPlayableBehaviour.PrepareFrame()
│      ↓
│      targetFrame = floor(time * frameRate)
│      ↓
│      MultiCameraPointCloudManager.SeekToFrame(targetFrame)
│      ↓
│      Handler.SeekToFrame(targetFrame)
│
└─ BvhPlayableBehaviour.PrepareFrame()
       ↓
       targetFrame = floor(time * BvhFrameRate) + frameOffset
       ↓
       ApplyFrame(targetFrame)
       ↓
       ApplyDriftCorrection(time)
```

---

## 7. Key Implementation Differences

| Feature | PointCloud | BVH |
|---------|-----------|-----|
| **Frame Rate Source** | PlayableBehaviour.frameRate (fixed) | BvhData.FrameRate (from file) |
| **Frame Offset** | None | frameOffset (for sync with point cloud) |
| **OnGraphStart** | Does nothing (commented out reset) | Creates joint hierarchy |
| **Drift Correction** | None | Applied every frame |
| **Arrow Key Support** | Yes (PLY), No (Binary) | No (Timeline only) |
| **Update Trigger** | `playable.GetTime()` | `playable.GetTime()` |
| **Clamping** | None (relies on manager) | Explicit clamp [0, FrameCount-1] |
| **OnGraphStop** | None | Resets currentFrame to -1 |

---

## 8. Synchronization Mechanisms

### Timeline-Based Sync (Point Cloud ↔ BVH)
```
PlayableDirector.time = 2.5 seconds
    ↓
PointCloudPlayableBehaviour.PrepareFrame():
    targetFrame = floor(2.5 * 30) = 75
    SeekToFrame(75)
    
BvhPlayableBehaviour.PrepareFrame():
    targetFrame = floor(2.5 * 30) + frameOffset = 75 + frameOffset
    ApplyFrame(targetFrame)
```

**Both systems drive from same timeline time but:**
- PointCloud uses fixed frameRate (default 30)
- BVH uses BvhData.FrameRate (can differ)
- BVH has frameOffset for fine-tuning alignment

### Arrow Key Seeking (Point Cloud Only)
```
Left Arrow
    ↓
PlyModeHandler.LoadPreviousPlyFrame()
    ↓
MultiPointCloudView.LoadFromPLY(filePath)
    
[Timeline is NOT updated]
[BVH is NOT updated]
```

---

## 9. Implications for Frame-Accurate Navigation

**Issue:** BinaryModeHandler doesn't support left arrow (backward seeking)
- **Reason:** Backward frame reconstruction from raw binary data is expensive
- **Workaround:** Users must use Timeline scrubbing for backward seeking

**Solution Approaches:**
1. Cache last N frames for fast backward seeking
2. Implement timeline scrubbing in arrow keys (seek via `PlayableDirector.time`)
3. Pre-process and cache frame indices for fast random access

**Current Recommendation from Code:**
```csharp
Debug.LogWarning("Backward navigation not supported. Use timeline controls.");
```

---

## Files Involved

| File | Purpose |
|------|---------|
| `Assets/Script/timeline/TimelineController.cs` | Timeline playback control (Space, Escape, Shift+A, Shift+U) |
| `Assets/Script/timeline/PointCloudPlayableBehaviour.cs` | Sync point cloud to timeline time |
| `Assets/Script/timeline/BvhPlayableBehaviour.cs` | Sync BVH to timeline time + apply drift correction |
| `Assets/Script/timeline/BvhPlayableAsset.cs` | BVH asset with keyframe recording helpers |
| `Assets/Script/pointcloud/handler/PlyModeHandler.cs` | Handle left/right arrow keys for PLY mode |
| `Assets/Script/pointcloud/handler/BinaryModeHandler.cs` | Handle right arrow only, warn on left arrow |
| `Assets/Script/pointcloud/MultiCamPointCloudManager.cs` | Delegate seeking to active handler |
| `Assets/Script/utils/BvhDriftCorrectionData.cs` | Keyframe storage and management |
| `Assets/Script/utils/BvhKeyframe.cs` | Individual keyframe data structure |

# Timeline Seeking Implementation Comparison

## Side-by-Side: PointCloud vs BVH PrepareFrame()

### PointCloudPlayableBehaviour.PrepareFrame()
```csharp
public override void PrepareFrame(Playable playable, FrameData info)
{
    if (pointCloudManager == null) return;
    
    double currentTime = playable.GetTime();                    // Get timeline time
    int targetFrame = Mathf.FloorToInt((float)(currentTime * frameRate)); // Convert to frame
    
    if (targetFrame != currentFrame)  // Only if frame changed
    {
        pointCloudManager.SeekToFrame(targetFrame);            // Seek
        currentFrame = targetFrame;
    }
}
```

**Characteristics:**
- Simple linear conversion: time * frameRate
- No frame offset
- No clamping
- Optimization: Skip update if frame unchanged

---

### BvhPlayableBehaviour.PrepareFrame()
```csharp
public override void PrepareFrame(Playable playable, FrameData info)
{
    if (bvhData == null || targetTransform == null) return;

    // Get current time from timeline
    double currentTime = playable.GetTime();                          // Get timeline time

    // Calculate frame based on BVH's frame rate
    float bvhFrameRate = bvhData.FrameRate;                         // Dynamic frame rate
    int targetFrame = Mathf.FloorToInt((float)(currentTime * bvhFrameRate)); // Convert

    // Apply frame offset for synchronization with point cloud
    targetFrame += frameOffset;                                      // Alignment offset

    // Clamp to valid range
    targetFrame = Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1); // Safety clamp

    // Only update if frame changed
    if (targetFrame != currentFrame)
    {
        ApplyFrame(targetFrame);                                      // Apply BVH frame
        currentFrame = targetFrame;
    }

    // Apply drift correction if enabled
    ApplyDriftCorrection((float)currentTime);                        // Always apply
}
```

**Characteristics:**
- Dynamic frame rate from BVH data
- Frame offset support
- Explicit clamping
- Optimization: Skip apply if frame unchanged
- Always applies drift correction (regardless)

---

## Side-by-Side: OnGraphStart()

### PointCloudPlayableBehaviour.OnGraphStart()
```csharp
public override void OnGraphStart(Playable playable)
{
    // Don't auto-reset to first frame - let user control timeline position
    // if (pointCloudManager != null)
    // {
    //     pointCloudManager.ResetToFirstFrame();
    // }
}
```

**Action:** None (intentionally)

---

### BvhPlayableBehaviour.OnGraphStart()
```csharp
public override void OnGraphStart(Playable playable)
{
    if (bvhData != null && targetTransform != null)
    {
        // Save the initial position of BVH_Character as baseline for drift correction
        positionOffset = targetTransform.localPosition;
        Debug.Log($"[BvhPlayableBehaviour] OnGraphStart: Saved BVH_Character initial position as positionOffset: {positionOffset}");

        // Cache joint hierarchy
        var jointList = bvhData.GetAllJoints();
        joints = jointList.ToArray();
        jointTransforms = new Transform[joints.Length];

        // Create or find transforms for all joints
        CreateJointHierarchy();  // <-- BUILD ENTIRE SKELETON
    }
}
```

**Actions:**
1. Save initial position for drift correction baseline
2. Cache joint data
3. Create entire skeleton hierarchy

---

## Arrow Key Handling Comparison

### PlyModeHandler.HandlePlyModeNavigation()
```csharp
private void HandlePlyModeNavigation()
{
    if (Keyboard.current == null) return;

    if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
    {
        LoadNextPlyFrame();        // IMPLEMENTED
    }

    if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
    {
        LoadPreviousPlyFrame();     // IMPLEMENTED
    }
}
```

**Support:** LEFT: Yes, RIGHT: Yes

---

### BinaryModeHandler.HandleSynchronizedFrameNavigation()
```csharp
private void HandleSynchronizedFrameNavigation()
{
    if (Keyboard.current == null) return;

    if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
    {
        NavigateToNextSynchronizedFrame();   // IMPLEMENTED
    }

    if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
    {
        Debug.LogWarning("Backward navigation not supported. Use timeline controls.");
        // NOT IMPLEMENTED
    }
}
```

**Support:** LEFT: No, RIGHT: Yes

---

## Frame Calculation Formulas

### PointCloud
```
targetFrame = floor(playable.GetTime() * 30.0f)
```
- Uses fixed frameRate property (default 30)
- No offset
- No bounds checking

### BVH
```
targetFrame = floor(playable.GetTime() * bvhData.FrameRate) + frameOffset
targetFrame = clamp(targetFrame, 0, bvhData.FrameCount - 1)
```
- Uses dynamic frame rate from BVH file
- Adds frameOffset for synchronization
- Explicit bounds checking

---

## Drift Correction Pipeline

Only implemented in BvhPlayableBehaviour:

```csharp
private void ApplyDriftCorrection(float timelineTime)
{
    if (driftCorrectionData == null || !driftCorrectionData.IsEnabled)
        return;

    if (bvhData == null || targetTransform == null)
        return;

    // Get target anchor position from keyframe interpolation
    Vector3 targetAnchorPositionRelative = driftCorrectionData.GetAnchorPositionAtTime(timelineTime);

    // Find root joint transform
    Transform rootJointTransform = targetTransform.Find(bvhData.RootJoint.Name);
    if (rootJointTransform == null)
        return;

    // Apply correction: baseline position (positionOffset) + anchor correction
    targetTransform.localPosition = positionOffset + targetAnchorPositionRelative;
}
```

**Key Points:**
- Called EVERY frame in PrepareFrame()
- Uses keyframe-based interpolation
- Corrects root transform position
- Baseline reference: positionOffset (saved at OnGraphStart)

---

## Synchronization Strategy

### Timeline-Based Sync
Both systems respond to the same `playable.GetTime()` value:

```
Timeline time = 2.5 seconds
    |
    +-- PointCloud: frame = floor(2.5 * 30) = 75
    |   
    +-- BVH: frame = floor(2.5 * 30) + frameOffset = 75 + offset
    |         (Then applies drift correction interpolation)
```

**Potential Issue:** Different frame rates
- PointCloud: 30fps (fixed)
- BVH: Variable (from file)
- **Solution:** Use frameOffset to fine-tune alignment

### Arrow Key Seeking
Only affects PointCloud, not Timeline or BVH:

```
Left/Right Arrow
    |
    +-- PlyModeHandler.LoadPlyFrame()
    |   Point cloud updates immediately
    |   Timeline.time NOT changed
    |   BVH NOT updated
```

**Result:** Desynchronization possible if arrow keys used

---

## Configuration Points

### PointCloudPlayableBehaviour
- `frameRate`: Fixed frame rate (default 30)
- Set in: PlayableAsset or Behaviour Inspector

### BvhPlayableBehaviour
- `frameRate`: (Not used - overridden by bvhData.FrameRate)
- `frameOffset`: Synchronization offset (in frames)
- `positionOffset`: Initial position (auto-saved at OnGraphStart)
- `rotationOffset`: Root joint rotation adjustment
- `scale`: Animation scale factor
- `applyRootMotion`: Whether to use root animation data
- `driftCorrectionData`: Reference to drift correction keyframes
- Set in: PlayableAsset or Behaviour Inspector

**All configurable via Inspector on the BVH clip in Timeline**

---

## Update Frequency Comparison

| Trigger | PointCloud | BVH |
|---------|-----------|-----|
| PrepareFrame called | Every frame | Every frame |
| Frame calculation | Every frame | Every frame |
| Frame apply | Only if changed | Only if changed |
| Drift correction | N/A | Every frame (always) |

---

## Error Handling

### PointCloudPlayableBehaviour
```csharp
if (pointCloudManager == null) return;
// Relies on handler for frame bounds checking
```
- Minimal validation
- Delegates bounds checking to handler

### BvhPlayableBehaviour
```csharp
if (bvhData == null || targetTransform == null) return;
// ...
targetFrame = Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1);
```
- Validates prerequisites
- Explicit bounds checking
- Safe fallback on missing data

---

## Keyframe Recording Pipeline (Shift+A / Shift+U)

### Shift+A: Add Keyframe Path
```
Keyboard Input (Shift+A)
    |
    +-- TimelineController.HandleInput()
        |
        +-- AddDriftCorrectionKeyframe()
            |
            +-- Get current timeline time: playable.GetTime()
            +-- Get current BVH frame: BvhPlayableBehaviour.GetCurrentFrame()
            +-- Get BVH position: BvhPlayableAsset.GetBvhCharacterPosition()
            |
            +-- BvhDriftCorrectionData.AddKeyframe(time, frame, position)
                |
                +-- Create new BvhKeyframe
                +-- Add to keyframes list
                +-- EditorUtility.SetDirty() - Mark asset dirty for save
                |
            +-- Debug log confirmation
```

### Shift+U: Update Keyframe Path
```
Keyboard Input (Shift+U)
    |
    +-- TimelineController.HandleInput()
        |
        +-- UpdateCurrentDriftCorrectionKeyframe()
            |
            +-- Get last edited keyframe: BvhDriftCorrectionData.GetLastEditedKeyframe()
            +-- Get current BVH position: BvhPlayableAsset.GetBvhCharacterPosition()
            +-- Get current BVH frame: BvhPlayableBehaviour.GetCurrentFrame()
            |
            +-- BvhDriftCorrectionData.UpdateKeyframe(id, time, frame, position)
                |
                +-- Find keyframe by ID
                +-- Update position/frame values
                +-- EditorUtility.SetDirty() - Mark asset dirty for save
                |
            +-- Debug log confirmation
```

### Real-time Position Reflection
```
After Shift+A or Shift+U:
    |
    +-- BvhPlayableBehaviour.PrepareFrame() (called every frame)
        |
        +-- ApplyDriftCorrection(timeline.time)
            |
            +-- BvhDriftCorrectionData.GetAnchorPositionAtTime()
                |
                +-- Interpolate between nearby keyframes
                +-- Apply corrected position to BVH_Character
                |
            +-- Result: Real-time visual update in Viewport
```

---

## Call Chain Summary

### Timeline Seeking Path
```
PlayableDirector.time = X
    |
    +-- Engine calls PrepareFrame() on all Playables
        |
        +-- PointCloudPlayableBehaviour
            |
            +-- MultiCameraPointCloudManager.SeekToFrame(frame)
                |
                +-- PlyModeHandler.SeekToFrame() or BinaryModeHandler.SeekToFrame()
                    |
                    +-- Handler loads/processes frame
        |
        +-- BvhPlayableBehaviour
            |
            +-- ApplyFrame() - Updates joint transforms
            |
            +-- ApplyDriftCorrection() - Adjusts root position via interpolation
```

### Arrow Key Path (PLY Mode Only)
```
Keyboard Input (Left/Right Arrow)
    |
    +-- PlyModeHandler.HandlePlyModeNavigation()
        |
        +-- LoadPlyFrame(nextFrameIndex)
            |
            +-- MultiPointCloudView.LoadFromPLY(filePath)
                |
                +-- Point cloud mesh updated

[Timeline and BVH NOT affected]
```

### Keyframe Addition/Update Path (Shift+A / Shift+U)
```
Keyboard Input (Shift+A or Shift+U)
    |
    +-- TimelineController.HandleInput()
        |
        +-- AddDriftCorrectionKeyframe() or UpdateCurrentDriftCorrectionKeyframe()
            |
            +-- BvhPlayableAsset helpers (GetCurrentFrame, GetBvhCharacterPosition)
            |
            +-- BvhDriftCorrectionData.AddKeyframe() or UpdateKeyframe()
                |
                +-- Update internal keyframe list
                +-- EditorUtility.SetDirty()
                |
            +-- Next PrepareFrame(): ApplyDriftCorrection() applies new keyframe
                |
                +-- Real-time visual feedback in Viewport
```


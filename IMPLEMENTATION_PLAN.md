# BVH Drift Correction & Frame Speed Adjustment Implementation Plan

## Project Overview
ShiibaNFTUnity では、BVH（Biovision Hierarchical）アニメーション再生時にドリフト補正とフレーム再生スピード調整機能を実装しています。

---

## Phase 1: BVH Drift Correction (✅ COMPLETED)

### 1.1 Keyframe Data Structure Design
- **Status**: ✅ Completed
- **Files**:
  - `BvhKeyframe.cs` - Individual keyframe data (time, frame number, anchor position)
  - `BvhDriftCorrectionData.cs` - ScriptableObject for managing keyframes
- **Features**:
  - Serializable keyframe storage
  - Linear interpolation between keyframes
  - Timeline-based position anchoring

### 1.2 Integration with Timeline System
- **Status**: ✅ Completed
- **Files Modified**:
  - `DatasetConfig.cs` - Added BvhDriftCorrectionData reference
  - `BvhPlayableBehaviour.cs` - Drift correction application in PrepareFrame()
  - `BvhPlayableAsset.cs` - Loading drift correction from DatasetConfig
- **Algorithm**:
  ```
  targetTransform.localPosition = positionOffset + targetAnchorPositionRelative
  ```
  - `positionOffset`: BVH_Character initial position (saved in OnGraphStart)
  - `targetAnchorPositionRelative`: Interpolated anchor position from keyframes

### 1.3 Editor UI & Keyframe Management
- **Status**: ✅ Completed
- **File**: `BvhDriftCorrectionDataEditor.cs`
- **Features**:
  - Inspector editor with custom keyframe list
  - Timeline jump buttons (click keyframe to seek Timeline)
  - Keyframe deletion
  - Position change detection
  - SHIFT+A hotkey for adding keyframes (via Scene handler)

---

## Phase 2: Frame Replay Speed Adjustment via Keyframe Mapping (🚀 IN PROGRESS)

### 2.1 Speed Adjustment Mechanism Design
- **Status**: 🔄 Planning
- **Goal**: Adjust BVH playback speed dynamically by mapping Timeline time to BVH frame numbers via keyframes
- **Key Concept**:
  - Each keyframe contains both `timelineTime` and `bvhFrameNumber`
  - These define anchor points in a Timeline-to-Frame mapping
  - Between keyframes, frame playback speed is automatically interpolated

**Example**:
```
Keyframe 0: timelineTime = 8.0s  → bvhFrameNumber = 247
Keyframe 1: timelineTime = 9.4s  → bvhFrameNumber = 290

Timeline duration: 9.4 - 8.0 = 1.4 seconds
BVH frames played: 290 - 247 = 43 frames
Playback speed: 43 frames / 1.4s ≈ 30.7 fps (vs default 30 fps)
```

### 2.2 Frame Calculation Algorithm (NEW)

**Current (linear mapping)**:
```csharp
int targetFrame = Mathf.FloorToInt((float)(currentTime * bvhFrameRate));
targetFrame += frameOffset;
targetFrame = Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1);
```

**Proposed (keyframe-based mapping with extrapolation)**:
```csharp
// Find surrounding keyframes for current Timeline time
BvhKeyframe prevKeyframe = null;
BvhKeyframe nextKeyframe = null;

foreach (var kf in keyframes)
{
    if (kf.timelineTime <= currentTime)
        prevKeyframe = kf;
    else if (nextKeyframe == null)
        nextKeyframe = kf;
}

int targetFrame;

// Case 1: Between two keyframes - interpolate frame number
if (prevKeyframe != null && nextKeyframe != null)
{
    float timeDelta = nextKeyframe.timelineTime - prevKeyframe.timelineTime;
    if (timeDelta > 0)
    {
        float frameDelta = nextKeyframe.bvhFrameNumber - prevKeyframe.bvhFrameNumber;
        float t = (currentTime - prevKeyframe.timelineTime) / timeDelta;
        t = Mathf.Clamp01(t);

        targetFrame = Mathf.FloorToInt(prevKeyframe.bvhFrameNumber + (frameDelta * t));
    }
    else
    {
        targetFrame = prevKeyframe.bvhFrameNumber;
    }
}
// Case 2: Before first keyframe - interpolate from (0s, frame 0) to first keyframe
else if (prevKeyframe == null && nextKeyframe != null)
{
    float timeDelta = nextKeyframe.timelineTime - 0f;
    if (timeDelta > 0)
    {
        float frameDelta = nextKeyframe.bvhFrameNumber - 0;
        float t = (currentTime - 0f) / timeDelta;
        t = Mathf.Clamp01(t);

        targetFrame = Mathf.FloorToInt(0 + (frameDelta * t));
    }
    else
    {
        targetFrame = 0;
    }
}
// Case 3: After last keyframe - extrapolate using BVH file's native frame rate
else if (prevKeyframe != null && nextKeyframe == null)
{
    float timeSincePrevKeyframe = currentTime - prevKeyframe.timelineTime;
    float additionalFrames = timeSincePrevKeyframe * bvhFrameRate;  // Use BVH file's frame rate

    targetFrame = prevKeyframe.bvhFrameNumber + Mathf.FloorToInt(additionalFrames);
}
// Case 4: No keyframes available - fall back to linear mapping
else
{
    targetFrame = Mathf.FloorToInt((float)(currentTime * bvhFrameRate));
}

// Apply frame offset after all interpolations
targetFrame += frameOffset;
targetFrame = Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1);
```

**Key Points:**
- `bvhFrameRate` comes from `BvhData.FrameRate` (calculated from BVH file's `Frametime` property)
- **Case 1**: Between keyframes - smooth speed adjustment via frame interpolation
- **Case 2**: Before first keyframe - assume animation starts at frame 0 at time 0s
- **Case 3**: After last keyframe - continues with BVH file's native frame rate (important!)
- **Case 4**: No keyframes - falls back to original linear mapping
- Frame offset applies after all interpolations, before clamping

### 2.3 Implementation Strategy
- **Modification**: `BvhPlayableBehaviour.PrepareFrame()`
  - Use `driftCorrectionData` keyframes for frame mapping
  - Already have keyframes with both `timelineTime` and `bvhFrameNumber`
  - No new component needed - leverage existing keyframe system

- **No new files required**:
  - ✅ BvhDriftCorrectionData already contains keyframes
  - ✅ BvhKeyframe already has timelineTime and bvhFrameNumber
  - ✅ Frame mapping logic can be added to BvhPlayableBehaviour

### 2.4 Keyframe Setup for Speed Control
- **Manual**: Edit keyframes in Inspector, adjusting `bvhFrameNumber` to control speed
  - Default mapping: `bvhFrameNumber ≈ timelineTime * 30` (assuming 30 fps)
  - Speed 2x: `bvhFrameNumber ≈ timelineTime * 60`
  - Speed 0.5x: `bvhFrameNumber ≈ timelineTime * 15`

- **Workflow**:
  1. Create keyframes at desired Timeline positions (SHIFT+A)
  2. Adjust `bvhFrameNumber` values in Inspector to control speed between keyframes
  3. Timeline playback automatically maps to BVH frames based on keyframe mapping

### 2.5 Synchronization Considerations
- ✅ Drift correction keyframes = Speed adjustment keyframes (same data)
- ✅ Frame offset still applies after frame interpolation
- ✅ Smooth speed transitions without frame skipping (continuous interpolation)
- ✅ Different speed sections can be defined per Timeline segment

---

## Phase 3: Depth Flow Calculation (📋 PLANNED)

### 3.1 Concept
Calculate per-frame depth flow (point-to-bone distance mapping) for motion capture analysis.

### 3.2 Components (Planned)
- `DepthFlowCalculator.cs` - Core depth flow calculation
- `DepthFlowVisualizer.cs` - Real-time visualization
- Shader for GPU-accelerated calculation (optional)

### 3.3 Data Flow
```
Point Cloud Data → Distance to Bones → Depth Flow Map → Visualization
```

---

## File Structure Summary

```
Assets/Script/
├── utils/
│   ├── BvhKeyframe.cs                    ✅ Keyframe data structure
│   ├── BvhDriftCorrectionData.cs         ✅ Keyframe manager ScriptableObject
│   ├── Editor/
│   │   └── BvhDriftCorrectionDataEditor.cs  ✅ Inspector UI & hotkeys
│   └── BvhSpeedController.cs             🚀 (To be implemented)
│
├── config/
│   └── DatasetConfig.cs                  ✅ (Modified for drift correction)
│
└── timeline/
    ├── BvhPlayableAsset.cs               ✅ (Modified for drift correction)
    ├── BvhPlayableBehaviour.cs           ✅ (Modified for drift correction & 🚀 speed)
    └── BvhSpeedPlayableAsset.cs          🚀 (Optional: separate speed control track)
```

---

## Testing Checklist

### Phase 1 (Drift Correction) ✅
- [ ] BvhDriftCorrectionData creates and serializes correctly
- [ ] Keyframes can be added via Inspector button
- [ ] Keyframes can be added via SHIFT+A hotkey
- [ ] Timeline jumps to keyframe when clicked
- [ ] Interpolation works between two keyframes
- [ ] BVH_Character drifts to anchored positions during Timeline playback
- [ ] Drift correction preserves initial position offset

### Phase 2 (Speed Adjustment) 🚀
- [ ] Speed multiplier can be set to 0.5x, 1.0x, 2.0x, etc.
- [ ] Frame calculation respects speed multiplier
- [ ] No frame skipping at different speeds
- [ ] Drift correction keyframes still apply correctly
- [ ] Frame offset still works independently

### Phase 3 (Depth Flow) 📋
- [ ] TBD

---

## Known Issues & Considerations

1. **Drift Correction**:
   - ✅ Fixed: Accumulation bug (now using baseline + correction model)
   - ✅ Fixed: positionOffset now stores initial position properly
   - No DEBUG logs that impact performance yet (can be optimized)

2. **Speed Adjustment**:
   - Frame rate assumption (30 fps default) - may need dynamic detection
   - Timeline playback rate vs BVH frame rate mismatch handling
   - Smooth transitions during speed changes

3. **Integration**:
   - MultiCameraPointCloudManager dependency for DatasetConfig
   - Point cloud and BVH speed synchronization

---

## Next Steps (Immediate)

1. ✅ Complete Phase 1 (Drift Correction) - DONE
2. 🚀 **START Phase 2 (Speed Adjustment)**:
   - Create `BvhSpeedController.cs`
   - Modify frame calculation in `BvhPlayableBehaviour.PrepareFrame()`
   - Add Inspector UI controls
   - Test frame playback at different speeds

3. 📋 Plan Phase 3 (Depth Flow) after Phase 2 completion

---

## References

- **Timeline System**: Unity Timeline 1.8.7 documentation
- **PlayableBehaviour**: Frame-by-frame control via PrepareFrame callback
- **BVH Format**: Biovision motion capture skeletal animation format
- **DatasetConfig**: Central configuration for all dataset-related settings

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

## Phase 2: Frame Replay Speed Adjustment via Keyframe Mapping (✅ COMPLETED)

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

## Phase 3: Scene Flow Calculation (🚀 IN PROGRESS)

### 3.0 Linked-List History Architecture (2025-11-25)

**Major Design Decision**: Refactored SceneFlowCalculator to use **linked-list history** structure instead of simple vector storage.

**Motivation**:
- User pressed button to calculate scene flow, triggering backward-time history building
- History depth controlled by Inspector parameter `historyFrameCount` (1-1000 frames, default 100)
- Must account for keyframe-based speed adjustments in BvhPlayableBehaviour when traversing history

**Implementation**:
```csharp
// Each BoneSegmentPoint references the previous frame's segment
public class BoneSegmentPoint {
    public int boneIndex;
    public int segmentIndex;
    public int frameIndex;
    public Vector3 position;
    public BoneSegmentPoint previousPoint;  // Linked-list reference (backward in time)
    public Vector3 motionVector;             // Current - Previous position
}

// Rolling buffer maintains history chain
// UpdateFrameSegments():
//   previousFrameSegments = boneSegments  // Preserve current as previous
//   boneSegments = new List<...>          // Allocate new current frame
//   frameHistoryDepth = Min(depth+1, historyFrameCount)
```

**Benefits**:
- Memory efficient: only `historyFrameCount` frames stored (default 100)
- Direct temporal chain: traverse backward by following `previousPoint` references
- Flexible: history depth configurable via Inspector
- Clean: no separate frameIndex-based lookup arrays needed

**Implementation Status** (2025-11-25):
- ✅ **Tasks 6-9**: On-demand backtracking fully implemented
  - `BuildFrameHistoryBacktrack()` - Orchestrator for history building
  - `ApplyBvhFrame()` - BVH frame data extraction and application
  - `UpdateSegmentPositions()` - Segment point generation (100 per bone)
  - `LinkSegmentHistory()` - LinkedList chaining + motion vector calculation
  - `FrameHistoryEntry` - Data structure for storing frame data
  - All methods compile without errors ✅

**Next Phase**: Point cloud processing (Tasks 10-12) to map points to nearest segments and accumulate motion.

### 3.1 Concept
Calculate per-frame scene flow (point-to-bone distance mapping) for motion capture analysis.

### 3.2 Components (Planned)
- `SceneFlowCalculator.cs` - Core scene flow calculation with linked-list history
- `SceneFlowVisualizer.cs` - Real-time visualization
- Shader for GPU-accelerated calculation (optional)

### 3.3 Data Flow
```
Timeline Button → CalculateSceneFlowForCurrentFrame() → UpdateFrameSegments() → LinkedList History Build → Scene Flow Analysis
     ↓
Point Cloud Data → Distance to Bones → Scene Flow Map → Visualization
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

### Phase 3 (Scene Flow) 📋
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

## Next Steps (Immediate) - Phase 3 Implementation Tasks

### 3.8 Detailed Implementation Breakdown

#### Core Class Structure (Tasks 1-5)

- [x] **Task 1: Create SceneFlowCalculator.cs core class structure** ✅ COMPLETED
  - File: `Assets/Script/sceneflow/SceneFlowCalculator.cs`
  - ✅ Define main `SceneFlowCalculator` class with MonoBehaviour
  - ✅ Add configuration fields: `segmentsPerBone` (default 100), `debugMode`, `historyFrameCount` (default 100, range 1-1000)
  - ✅ Create internal storage for bone transforms, segments, and point flows
  - ✅ Added button method `CalculateSceneFlowForCurrentFrame()` with `[ContextMenu]`
  - ✅ Added `SetFrameInfo()` for frame tracking
  - Description: Set up the basic skeleton of the SceneFlowCalculator class with linked-list history support

- [x] **Task 2: Implement BoneSegmentPoint and PointSceneFlow data structures** ✅ COMPLETED (REFACTORED for linked-list history)
  - ✅ Add `BoneSegmentPoint` class with fields:
    - `boneIndex`, `segmentIndex`, `frameIndex` (tracking)
    - `position` (Vector3, current frame)
    - `previousPoint` (BoneSegmentPoint?, linked-list to past frames)
    - `motionVector` (Vector3, position - previousPoint.position)
  - ✅ Add `PointSceneFlow` class with fields: `position`, `nearestSegmentIndex`, `distanceToSegment`, `currentMotionVector`, `cumulativeMotionVector`
  - ✅ Linked-list chain structure for backward-temporal history
  - Description: Data structures with linked-list history for tracking motion across frames

- [x] **Task 3: Implement GatherBoneTransforms() to collect all bone Transforms** ✅ COMPLETED
  - ✅ Recursively traverse BVH hierarchy from root
  - ✅ Collect all Transform components in depth-first order
  - ✅ Store in `boneTransforms` list
  - Description: Walk the BVH skeleton tree and gather all bone Transform references for processing

- [x] **Task 4: Implement Initialize() method for SceneFlowCalculator setup** ✅ COMPLETED
  - ✅ Accept `BvhData` and `Transform bvhCharacterRoot` parameters
  - ✅ Call `GatherBoneTransforms()` to populate bone list
  - ✅ Allocate `BoneSegmentPoint[100]` arrays for each bone in `boneSegments` list
  - ✅ Initialize `pointFlows` list
  - Description: Set up the calculator with BVH data and prepare internal data structures

- [x] **Task 5: Implement UpdateBoneSegments() for per-frame segment point generation** ✅ COMPLETED (REFACTORED for linked-list)
  - ✅ For each bone, get world positions of start (parent joint) and end (current joint)
  - ✅ Generate 100 uniformly distributed points along each bone via Lerp
  - ✅ Link current frame segments to previous frame segments via `previousPoint` reference
  - ✅ Calculate motion vectors as `current.position - previousPoint.position`
  - ✅ Build backward-linked chain for historical motion tracking
  - Description: Compute segment points per bone with linked-list chain to past frames

#### On-Demand History Backtracking (Tasks 6-11 REFACTORED - 2025-11-25)

**Architecture Change**: Moved from continuous frame updates to **on-demand backtracking on button press**

- [x] **Task 6: Implement BuildFrameHistoryBacktrack()** ✅ COMPLETED
  - ✅ Backtrack from current BVH frame through `historyFrameCount` frames
  - ✅ For each frame: extract BVH data → apply to bones → generate segments → store in `FrameHistoryEntry`
  - ✅ Build linked-list by linking segments across frames
  - Implementation:
    ```
    CalculateSceneFlowForCurrentFrame() [Button Press]
      ↓
    BuildFrameHistoryBacktrack(currentBvhFrame)
      ├─ ApplyBvhFrame(frame) - Extract BVH frame data, apply to bone transforms
      ├─ UpdateSegmentPositions(boneIdx, segments) - Generate 100 Lerp points per bone
      ├─ FrameHistoryEntry created with bvhFrameNumber, timelineTime, segments[][]
      └─ LinkSegmentHistory() - Chain previousPoint across frames, calc motionVector
    ```
  - **Key Data Structures**:
    - `frameHistory: List<FrameHistoryEntry>` - Stores all history entries (oldest to newest)
    - `FrameHistoryEntry.segments[boneIdx][segIdx]` - 2D array of segment points per frame
    - `BoneSegmentPoint.previousPoint → FrameHistoryEntry[i-1].segments[...]` - LinkedList chain
  - Description: On-demand backward history construction triggered by button press

- [x] **Task 7: Implement ApplyBvhFrame()** ✅ COMPLETED
  - ✅ Extract BVH frame data using `bvhData.GetFrame(frameIndex)`
  - ✅ Parse channels in depth-first joint order
  - ✅ Apply position/rotation to bone Transform hierarchy
  - Description: Restore bone transforms to specific BVH frame state

- [x] **Task 8: Implement UpdateSegmentPositions()** ✅ COMPLETED
  - ✅ Get bone endpoints (parent joint, current joint) in world space
  - ✅ Generate 100 uniformly distributed points via Lerp along bone
  - Description: Create segment point cloud for a single bone

- [x] **Task 9: Implement LinkSegmentHistory()** ✅ COMPLETED
  - ✅ For each adjacent frame pair in `frameHistory`
  - ✅ Link current frame's `previousPoint` → previous frame's segment
  - ✅ Calculate `motionVector = current.position - previous.position`
  - Description: Build linked-list chain and compute motion vectors

#### Point Cloud Processing (Tasks 10-12)

- [ ] **Task 10: Implement FindNearestSegment() for point-to-segment mapping**
  - For a given point, search all bones and all segments (100 per bone)
  - Calculate distance to each segment point
  - Track minimum distance and nearest segment index
  - Update `PointSceneFlow` with nearest segment info and motion vector
  - Description: Find the closest bone segment point for each point cloud point and assign its motion vector

- [ ] **Task 11: Implement CalculatePointFlows() to process point clouds**
  - Accept `Vector3[] pointPositions` array of point cloud vertices
  - For each point, create `PointSceneFlow` instance
  - Call `FindNearestSegment()` for each point
  - Optionally accept `Vector3[] cumulativeFlows` for resuming calculations
  - Description: Main processing function that maps all point cloud points to their nearest bone segments

- [ ] **Task 12: Implement AccumulateMotion() for cumulative flow calculation**
  - For each point in `pointFlows`, add current motion vector to cumulative vector
  - Called once per frame during Timeline playback
  - Description: Accumulate motion vectors over multiple frames to track total displacement

#### Export & Utility Methods (Tasks 13-14)

- [ ] **Task 13: Implement GetCumulativeMotionVectors() export method**
  - Extract `cumulativeMotionVector` from all `PointSceneFlow` instances
  - Return as `Vector3[]` array for export or visualization
  - Description: Export cumulative scene flow data for further processing or file output

- [ ] **Task 14: Implement DrawDebugVisualization() for visualization**
  - Draw bone segment lines in green (connecting 100 points per bone)
  - Draw point-to-segment associations in blue (from point to nearest segment)
  - Draw motion vectors in red (from segment showing direction and magnitude)
  - Only active when `debugMode = true`
  - Description: Visual debugging tool to verify segment generation and point mappings

#### Integration Tasks (Tasks 15-17)

- [ ] **Task 15: Add SceneFlowCalculator configuration to DatasetConfig.cs**
  - Add `[SerializeField] bool enableSceneFlowCalculation = false`
  - Add `[SerializeField] int segmentsPerBone = 100`
  - Add `[SerializeField] string sceneFlowOutputPath = "Assets/Output/SceneFlow"`
  - Description: Expose scene flow settings in the central configuration ScriptableObject

- [ ] **Task 16: Integrate SceneFlowCalculator with TimelineController.cs**
  - Create SceneFlowCalculator instance in TimelineController
  - Call `Initialize()` when BVH data is loaded
  - Call `SetFrameInfo()` to update current BVH frame from BvhPlayableBehaviour
  - Provide access to `CalculateSceneFlowForCurrentFrame()` button (optional)
  - Description: Connect scene flow calculator initialization to Timeline system

- [ ] **Task 17: Integrate SceneFlowCalculator with MultiCameraPointCloudManager.cs**
  - Get `Mesh.vertices` from current point cloud view
  - Pass to `SceneFlowCalculator.CalculatePointFlows()`
  - Cache and store results for export
  - Description: Connect point cloud data to the scene flow calculator

#### Testing & Optimization Tasks (Tasks 18-22)

- [ ] **Task 18: Create unit tests for SceneFlowCalculator core functions**
  - Test `Initialize()` correctly populates bone transforms
  - Test `BuildFrameHistoryBacktrack()` generates correct frame count
  - Test `ApplyBvhFrame()` correctly applies frame data
  - Test `LinkSegmentHistory()` correctly chains segments
  - Test `FindNearestSegment()` finds correct nearest segment
  - Description: Unit tests to verify core algorithm correctness

- [ ] **Task 19: Test with sample BVH and point cloud data**
  - Load sample BVH file with known bone structure
  - Create test point cloud with known positions
  - Verify frame history backtracking generates correct frames
  - Check segment generation and point mapping visually
  - Validate motion vectors are calculated correctly
  - Description: Integration testing with real data to validate end-to-end functionality

- [ ] **Task 20: Create SceneFlowVisualizer.cs (optional visualization)**
  - Render bone segment chains in different colors per frame
  - Draw point-to-segment associations with distance indicators
  - Render motion vectors as colored arrows in 3D space
  - Color-code by magnitude (red=high, blue=low)
  - Real-time toggle for different visualization modes
  - Description: Real-time visualization component for inspecting scene flow results

- [ ] **Task 21: Implement export functionality for scene flow results**
  - Export frame history to JSON with all segment positions
  - Export motion vectors as PLY with velocity properties
  - Export cumulative flow as CSV for analysis
  - Description: Export scene flow data for external processing and analysis

- [ ] **Task 22: Performance optimization and benchmarking**
  - Profile with different point cloud sizes (100K, 1M, 10M points)
  - Optimize `FindNearestSegment()` with spatial acceleration if needed (KD-tree, grid)
  - Consider GPU compute shader for large datasets
  - Document performance characteristics
  - Description: Ensure acceptable performance across target point cloud sizes

---

## References

- **Timeline System**: Unity Timeline 1.8.7 documentation
- **PlayableBehaviour**: Frame-by-frame control via PrepareFrame callback
- **BVH Format**: Biovision motion capture skeletal animation format
- **DatasetConfig**: Central configuration for all dataset-related settings

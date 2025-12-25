# Scene Flow PLY Export Guide

## How to Export All PLY Files with Scene Flow (Motion Vectors)

### Setup Steps

1. **Add SceneFlowBatchExporter to Scene**
   - In Unity Editor, find the GameObject with `SceneFlow` component (or create a new one)
   - Add Component → `SceneFlowBatchExporter`
   - The component will auto-find `SceneFlowCalculator` and `MultiPointCloudView`

2. **Configure Export Settings** (in SceneFlowBatchExporter Inspector)
   - **Output Directory**: `ExportedPLY_SceneFlow` (default) - relative to project root
   - **Start Frame**: `1` (frame 1, not 0, since we need previous frame for motion vectors)
   - **End Frame**: `0` (0 = use total frame count)
   - **Export As ASCII**: `false` (use binary format by default)
   - **Skip Existing Files**: `true` (recommended for resuming interrupted exports)

### Export Methods

#### Method 1: Using DatasetConfig Inspector (Recommended)

1. **Enter Play Mode** in Unity
2. **Select the DatasetConfig asset** in Project window
3. **In Inspector**, scroll to "PLY Export Controls" section
4. **Click "Export All Frames with Scene Flow"** button
5. Confirm the export dialog
6. Monitor progress in Console

#### Method 2: Using SceneFlowBatchExporter Inspector

1. **Enter Play Mode** in Unity
2. **Select the GameObject** with SceneFlowBatchExporter component
3. **In Inspector**, click the "Start Batch Export" button (if available in custom editor)
4. Or call `StartBatchExport()` from code

### Output Format

Exported PLY files will contain:

**Binary Format (default):**
```
ply
format binary_little_endian 1.0
element vertex [count]
property float x
property float y
property float z
property uchar red
property uchar green
property uchar blue
property float vx      ← Motion vector X
property float vy      ← Motion vector Y
property float vz      ← Motion vector Z
end_header
[binary data...]
```

**File naming:** `frame_XXXXXX_sceneflow.ply` (e.g., `frame_000001_sceneflow.ply`)

### Motion Vector Calculation

Motion vectors are calculated using:
1. **BVH skeletal animation** - bone positions at frame N and N-1
2. **Bone segmentation** - 100 uniformly distributed points per bone (configurable)
3. **Nearest neighbor matching** - each point cloud vertex is matched to nearest bone segment
4. **Drift correction** - BVH position/rotation offsets and keyframe corrections are applied

### Progress Monitoring

During export, watch the Console for progress messages:
```
[SceneFlowBatchExporter] Progress: 45.2% (123/272) - Elapsed: 67.3s, Remaining: ~81.5s
```

### Performance

- **Export speed**: ~0.5-2 seconds per frame (depends on point cloud size and bone complexity)
- **File size**: ~27 bytes per vertex (15 bytes position/color + 12 bytes motion vectors)
- **Example**: 50,000 points → ~1.3 MB per frame

### Troubleshooting

**Error: "SceneFlowBatchExporter not found"**
- Make sure you added the SceneFlowBatchExporter component to a GameObject in the scene

**Error: "BvhData not available"**
- Ensure MultiCameraPointCloudManager is in the scene and initialized
- BvhDataCache must be populated (happens automatically during Timeline playback)

**Error: "No bone segment data available"**
- Check that SceneFlowCalculator is properly configured
- Verify BVH data is loaded (check BvhDataCache)

**Motion vectors are all zero**
- Check that start frame > 0 (need previous frame for motion calculation)
- Verify BVH animation is loaded and playing

### Comparison with Regular PLY Export

| Feature | Regular Export (Shift+E) | Scene Flow Export |
|---------|-------------------------|-------------------|
| Position (x,y,z) | ✅ | ✅ |
| Color (r,g,b) | ✅ | ✅ |
| Motion Vectors (vx,vy,vz) | ❌ | ✅ |
| Export Method | Keyboard shortcut | Inspector button |
| Processing Mode | Binary mode only | Any mode |
| Frame Requirement | Current frame | Current + Previous |

### Advanced Usage

**Custom Frame Range:**
```csharp
// In code
SceneFlowBatchExporter exporter = GetComponent<SceneFlowBatchExporter>();
exporter.startFrame = 50;
exporter.endFrame = 150;
exporter.StartBatchExport();
```

**ASCII Export for Debugging:**
- Enable "Export As ASCII" in Inspector
- Files will be human-readable text format
- Larger file size (~3x bigger)

**Resume Interrupted Export:**
- Keep "Skip Existing Files" enabled
- Run export again - already exported files will be skipped
- Useful for long exports or after crashes

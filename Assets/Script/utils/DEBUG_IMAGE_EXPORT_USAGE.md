# Debug Image Export Feature

This feature allows you to export depth and color images from sensor binary data for debugging purposes.

## Purpose

When you notice strange color images or depth data from sensors, you can use this tool to:
- Export raw depth images as grayscale PNG files
- Export raw color images as PNG files
- Verify sensor data integrity
- Debug color/depth synchronization issues
- Inspect individual frames visually

## Usage Methods

### Method 1: Inspector Button (Recommended)

1. **Enter Play Mode** in Unity
2. **Select a SinglePointCloudView GameObject** in the hierarchy (e.g., `SinglePointCloudView_****`)
3. In the **Inspector**, scroll down to the **"Debug Image Export"** section
4. (Optional) Change the **Export Directory** path (default: `DebugImages` in project root)
5. Click **"Export Current Frame Images"** button
6. Images will be saved with filenames like:
   - `deviceName_depth_frame0_20231215_143022.png`
   - `deviceName_color_frame0_20231215_143022.png`

### Method 2: Script Call

You can call the export method from your own scripts:

```csharp
// Get reference to SinglePointCloudView
SinglePointCloudView view = GameObject.Find("SinglePointCloudView_Camera1").GetComponent<SinglePointCloudView>();

// Export current frame images to default directory
view.ExportDebugImages();

// Or specify custom directory
view.ExportDebugImages("/path/to/custom/directory");
```

### Method 3: Direct Utility Call

For more control, use `DebugImageExporter` directly:

```csharp
// Get sensor device from frame controller
SensorDevice device = frameController.Device;

// Export both depth and color
DebugImageExporter.ExportSensorImages(device, "DebugImages", frameIndex: 0);

// Or export individually
ushort[] depthValues = device.GetLatestDepthValues();
DebugImageExporter.ExportDepthImage(depthValues,
    device.GetDepthWidth(),
    device.GetDepthHeight(),
    "depth_output.png");

Texture2D colorTexture = device.GetLatestColorTexture();
DebugImageExporter.ExportColorTexture(colorTexture, "color_output.png");
```

## Output Format

### Depth Images
- **Format:** Grayscale PNG
- **Normalization:** Closer objects = brighter, farther objects = darker
- **Range:** 0-5000mm by default (configurable)
- **Pixel Value:** `brightness = 1.0 - (depth / maxDepth)`

### Color Images
- **Format:** RGB PNG
- **Source:** Directly from decoded JPEG data in sensor binary

## File Naming Convention

```
{deviceName}_depth_frame{frameIndex}_{timestamp}.png
{deviceName}_color_frame{frameIndex}_{timestamp}.png
```

Example:
```
camera_912422250887_depth_frame0_20231215_143022.png
camera_912422250887_color_frame0_20231215_143022.png
```

## Debugging Color Issues

If you notice strange colors in the point cloud:

1. **Export the current frame** using the Inspector button
2. **Check the color image** - does it look correct as a standalone image?
   - If NO: The issue is in the sensor data or JPEG decoding
   - If YES: The issue is in color projection or shader rendering
3. **Check the depth image** - are the depth values reasonable?
   - Black areas = very far or no data
   - White areas = very close objects
   - Gray = mid-range distances
4. **Compare multiple frames** to see if the issue is consistent or frame-specific

## Common Issues

### "Image export is only available in Play mode"
- You must be in Play mode and have processed at least one frame before exporting

### "Frame controller is not initialized"
- Wait for the scene to fully initialize after entering Play mode
- Ensure Timeline has started playback

### "No depth/color data available"
- The current frame hasn't been processed yet
- Try seeking to a different timestamp or playing the Timeline

## Advanced Configuration

### Custom Depth Normalization

```csharp
// Export depth with custom max range (e.g., 10 meters)
DebugImageExporter.ExportDepthImage(
    depthValues,
    width,
    height,
    "depth_10m_range.png",
    maxDepth: 10000  // 10,000mm = 10m
);
```

### Export During Timeline Playback

```csharp
// In your Timeline playback script
public class TimelineFrameExporter : MonoBehaviour
{
    public SinglePointCloudView[] views;
    public int exportEveryNFrames = 30;
    private int frameCounter = 0;

    void Update()
    {
        frameCounter++;
        if (frameCounter % exportEveryNFrames == 0)
        {
            foreach (var view in views)
            {
                view.ExportDebugImages($"TimelineExport/Frame{frameCounter}");
            }
        }
    }
}
```

## Performance Notes

- Image export is **synchronous** and will block the main thread briefly
- Exporting during playback may cause frame drops
- Consider exporting only when paused or at specific debugging moments
- PNG encoding is relatively fast but not instant for large images (640x480 typical)

## See Also

- [DebugImageExporter.cs](DebugImageExporter.cs) - Core export utility
- [SinglePointCloudViewEditor.cs](../pointcloud/view/Editor/SinglePointCloudViewEditor.cs) - Inspector UI
- [SinglePointCloudView.cs](../pointcloud/view/SinglePointCloudView.cs) - Public API method

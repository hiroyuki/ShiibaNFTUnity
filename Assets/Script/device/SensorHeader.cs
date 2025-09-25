using YamlDotNet.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class SensorHeader
{
    public List<RecordField> record_format { get; set; }
    public CustomData custom { get; set; }

    public int MetadataSize =>
        record_format
            .Where(f => f.name != "image")
            .Sum(f => GetTypeSize(f.type) * f.count);

    public int ImageSize =>
        record_format
            .Where(f => f.name == "image")
            .Sum(f => GetTypeSize(f.type) * f.count);

    private int GetTypeSize(string type)
    {
        return type switch
        {
            "u8" => 1,
            "u16" => 2,
            "u32" => 4,
            "u64" => 8,
            "i8" => 1,
            "i16" => 2,
            "i32" => 4,
            "i64" => 8,
            "f32" => 4,
            "f64" => 8,
            _ => throw new InvalidDataException($"Unknown type: {type}")
        };
    }
}

public class RecordField
{
    public string name { get; set; }
    public string comment { get; set; }
    public string type { get; set; }
    public int count { get; set; }
}

public class CustomData
{
    public int sensor_format_version { get; set; }
    public string device_type { get; set; }
    public string serial_number { get; set; }
    public string sensor_type { get; set; }
    public string sensor_name { get; set; }
    public string global_time_reference { get; set; }
    public CameraSensorMetadata camera_sensor { get; set; }
    public AdditionalInfoMetadata additional_info { get; set; }
}

public class CameraSensorMetadata
{
    public string shutter_type { get; set; }
    public bool srgb { get; set; }
    public float gamma_power { get; set; }
    public bool records_infrared_light { get; set; }
    public bool records_visible_light { get; set; }
    public string timestamp_reference { get; set; }
    public int fps { get; set; }
    public int width { get; set; }
    public int height { get; set; }
    public string format { get; set; }
}

public class AdditionalInfoMetadata
{
    public string orbbec_extrinsics_d2c_rotation { get; set; }
    public string orbbec_intrinsics_parameters { get; set; }
    public string orbbec_extrinsics_d2c_translation { get; set; }
    public string orbbec_sync_mode { get; set; }
    public string ycbcr_conversion_model { get; set; }
    public string ycbcr_chroma_filter { get; set; }
    public string ycbcr_range { get; set; }
    public string ycbcr_x_chroma_offset { get; set; }
    public string ycbcr_y_chroma_offset { get; set; }
    public bool ycbcr_force_explicit_reconstruction { get; set; }
}

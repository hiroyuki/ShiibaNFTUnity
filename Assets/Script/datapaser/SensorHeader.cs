using YamlDotNet.Serialization;
using System.Collections.Generic;public class SensorHeader
{
    public List<RecordField> record_format { get; set; }
    public CustomData custom { get; set; }
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
}

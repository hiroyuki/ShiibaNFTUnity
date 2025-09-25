using System.Collections.Generic;

public class HostInfo
{
    public string datasetUuid { get; set; }
    public string datasetInitialFolderName { get; set; }
    public List<HostDevice> devices { get; set; }
}

public class HostDevice
{
    public string deviceType { get; set; }
    public string serialNumber { get; set; }
    public string firmware { get; set; }

    public List<CameraSensor> cameraSensors { get; set; }
    public List<MicrophoneSensor> microphoneSensors { get; set; }
}

public class CameraSensor
{
    public string nameInDevice { get; set; }
}

public class MicrophoneSensor
{
    public string nameInDevice { get; set; }
}
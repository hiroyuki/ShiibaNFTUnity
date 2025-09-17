using System.IO;

public abstract class AbstractSensorDataParser : ISensorDataParser, System.IDisposable
{
    protected readonly BinaryReader reader;
    protected readonly string deviceName;

    public ulong CurrentTimestamp { get; protected set; }
    public SensorHeader sensorHeader { get; protected set; }

    public AbstractSensorDataParser(BinaryReader reader, string deviceName = "Unknown Device")
    {
        this.reader = reader;
        this.deviceName = deviceName;
    }

    public abstract string FormatIdentifier { get; }

    public abstract void ParseHeader();
    public abstract bool ParseNextRecord();
    public abstract bool ParseNextRecord(bool optimizeForGPU);
    public abstract void Dispose();

    public abstract bool PeekNextTimestamp(out ulong timestamp);

    // 将来的に共通ヘルパー関数などをここに追加できます
}

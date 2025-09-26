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
    public abstract bool ParseNextRecord(bool optimizeForGPU);
    public abstract void Dispose();

    public abstract bool PeekNextTimestamp(out ulong timestamp);

    /// <summary>
    /// 現在のレコードをスキップして次のレコードに移動
    /// 具象クラスで実装が必要
    /// </summary>
    public abstract bool SkipCurrentRecord();


}

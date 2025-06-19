using System;
using System.IO;
using System.Text;


public class RcsvSensorDataParser : ISensorDataParser, IDisposable
{
    private readonly BinaryReader _reader;
    public ulong IndexOffset { get; private set; }
    public ulong Unused { get; private set; }
    public uint HeaderSize { get; private set; }
    public string HeaderText { get; private set; }
    public string FormatIdentifier => "RCSV";

    public RcsvSensorDataParser(BinaryReader reader)
    {
        _reader = reader;
    }
    
    ~RcsvSensorDataParser()
    {
        Dispose();
    }
    public void Dispose()
    {
        _reader?.Dispose();
    }

    public void ParseHeader()
    {
        IndexOffset = _reader.ReadUInt64();
        Unused = _reader.ReadUInt64();
        HeaderSize = _reader.ReadUInt32();
        byte[] yamlBytes = _reader.ReadBytes((int)HeaderSize);
        HeaderText = Encoding.UTF8.GetString(yamlBytes);
    }

    public void ParseNextRecord()
    {
        // 可変長レコード処理（例：先頭にサイズがつく）
    }
}

using System.IO;
using System.Text;


public class RcsvSensorDataParser : ISensorDataParser
{
    public ulong IndexOffset { get; private set; }
    public ulong Unused { get; private set; }
    public uint HeaderSize { get; private set; }
    public string HeaderText { get; private set; }
    public string FormatIdentifier => "RCSV";
    public void ParseHeader(BinaryReader reader)
    {
        IndexOffset = reader.ReadUInt64();
        Unused = reader.ReadUInt64();
        HeaderSize = reader.ReadUInt32();
        byte[] yamlBytes = reader.ReadBytes((int)HeaderSize);
        HeaderText = Encoding.UTF8.GetString(yamlBytes);
    }

    public void ParseNextRecord(BinaryReader reader)
    {
        // 可変長レコード処理（例：先頭にサイズがつく）
    }
}

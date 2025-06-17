using System.IO;

public interface ISensorDataParser
{
    string FormatIdentifier { get; }
    void ParseHeader(BinaryReader reader);
    void ParseNextRecord(BinaryReader reader); // 実装時の拡張
}

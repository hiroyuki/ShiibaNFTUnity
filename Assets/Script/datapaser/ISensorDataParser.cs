using System.IO;

public interface ISensorDataParser
{
    string FormatIdentifier { get; }
    
    void ParseHeader();
    void ParseNextRecord(); // 実装時の拡張
}

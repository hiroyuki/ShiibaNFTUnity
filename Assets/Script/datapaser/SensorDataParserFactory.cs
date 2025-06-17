
using System.IO;
using System.Text;

public static class SensorDataParserFactory
{
    public static ISensorDataParser Create(string filePath)
    {
        using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using BinaryReader reader = new BinaryReader(fs);

        string ident = Encoding.ASCII.GetString(reader.ReadBytes(4));
        ISensorDataParser parser = ident switch
        {
            "RCST" => new RcstSensorDataParser(),
            "RCSV" => new RcsvSensorDataParser(),
            _ => throw new InvalidDataException($"Unknown file type: {ident}")
        };

        parser.ParseHeader(reader);
        return parser;
    }
}

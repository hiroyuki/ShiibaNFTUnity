
using System.IO;
using System.Text;

public static class SensorDataParserFactory
{
    public static ISensorDataParser Create(string filePath)
    {
        FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        BinaryReader reader = new BinaryReader(fs);

        string ident = Encoding.ASCII.GetString(reader.ReadBytes(4));
        ISensorDataParser parser = ident switch
        {
            "RCST" => new RcstSensorDataParser(reader),
            "RCSV" => new RcsvSensorDataParser(reader),
            _ => throw new InvalidDataException($"Unknown file type: {ident}")
        };

        parser.ParseHeader();
        return parser;
    }
}

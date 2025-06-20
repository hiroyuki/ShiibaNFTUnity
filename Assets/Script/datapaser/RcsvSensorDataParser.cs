using System;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;
using YamlDotNet.Serialization;

public class RcsvSensorDataParser : AbstractSensorDataParser
{
    public override string FormatIdentifier => "RCSV";
    public ulong IndexOffset { get; private set; }
    public ulong Unused { get; private set; }
    public uint HeaderSize { get; private set; }
    public string HeaderText { get; private set; }
    public byte[] CurrentColorBytes { get; private set; }
    public Color32[] CurrentColorPixels { get; private set; }

    private Texture2D _decodedTexture;
    private int _depthWidth = -1;
    private int _depthHeight = -1;

    public void SetTargetResolution(int width, int height)
    {
        _depthWidth = width;
        _depthHeight = height;
    }

    public RcsvSensorDataParser(BinaryReader reader) : base(reader) { }

    ~RcsvSensorDataParser() => Dispose();
    public override void Dispose() => reader?.Dispose();

    public override void ParseHeader()
    {
        IndexOffset = reader.ReadUInt64();
        Unused = reader.ReadUInt64();
        HeaderSize = reader.ReadUInt32();
        byte[] yamlBytes = reader.ReadBytes((int)HeaderSize);
        HeaderText = Encoding.UTF8.GetString(yamlBytes);

        var deserializer = new DeserializerBuilder().Build();
        sensorHeader = deserializer.Deserialize<SensorHeader>(HeaderText);
    }

    public override bool ParseNextRecord()
    {
        var colorField = sensorHeader.record_format.FirstOrDefault(f => f.name == "image");
        if (colorField == null)
        {
            Debug.LogError("record_format に 'image' フィールドがありません");
            return false;
        }

        int sizeTypeBytes = colorField.type switch
        {
            "u16" => 2,
            "u32" => 4,
            _ => throw new InvalidDataException($"Unsupported size type: {colorField.type}")
        };

        int metadataSize = sensorHeader.MetadataSize;
        byte[] headerAndSize = reader.ReadBytes(metadataSize + sizeTypeBytes);
        if (headerAndSize.Length < metadataSize + sizeTypeBytes) return false;

        CurrentTimestamp = BitConverter.ToUInt64(headerAndSize, 0);

        int imageSize = sizeTypeBytes == 2
            ? BitConverter.ToUInt16(headerAndSize, metadataSize)
            : BitConverter.ToInt32(headerAndSize, metadataSize);

        CurrentColorBytes = reader.ReadBytes(imageSize);
        if (CurrentColorBytes.Length != imageSize) return false;

        if (_decodedTexture == null)
            _decodedTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);

        if (_decodedTexture.LoadImage(CurrentColorBytes))
        {
            if (_depthWidth > 0 && _depthHeight > 0)
            {
                Texture2D resized = ResizeTexture(_decodedTexture, _depthWidth, _depthHeight);
                FlipTextureVertically(resized);
                CurrentColorPixels = resized.GetPixels32();
                UnityEngine.Object.Destroy(resized);
            }
            else
            {
                Debug.LogWarning("Depth resolution not set — skipping color resizing.");
                CurrentColorPixels = _decodedTexture.GetPixels32();
            }
        }

        return true;
    }

    private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
        RenderTexture.active = rt;
        Graphics.Blit(source, rt);

        Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }

    private void FlipTextureVertically(Texture2D tex)
    {
        Color[] pixels = tex.GetPixels();
        int width = tex.width;
        int height = tex.height;
        for (int y = 0; y < height / 2; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int top = y * width + x;
                int bottom = (height - 1 - y) * width + x;

                Color temp = pixels[top];
                pixels[top] = pixels[bottom];
                pixels[bottom] = temp;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
    }
    
    public override bool PeekNextTimestamp(out ulong timestamp)
{
    try
    {
        long originalPos = reader.BaseStream.Position;

        var colorField = sensorHeader.record_format.FirstOrDefault(f => f.name == "image");
        if (colorField == null)
        {
            timestamp = 0;
            return false;
        }

        int sizeTypeBytes = colorField.type switch
        {
            "u16" => 2,
            "u32" => 4,
            _ => throw new InvalidDataException($"Unsupported size type: {colorField.type}")
        };

        int metadataSize = sensorHeader.MetadataSize;
        byte[] header = reader.ReadBytes(metadataSize);
        if (header.Length < 8)
        {
            timestamp = 0;
            reader.BaseStream.Position = originalPos;
            return false;
        }

        timestamp = BitConverter.ToUInt64(header, 0);

        reader.BaseStream.Position = originalPos;
        return true;
    }
    catch
    {
        timestamp = 0;
        return false;
    }
}
}

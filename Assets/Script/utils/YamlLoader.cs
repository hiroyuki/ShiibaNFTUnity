using System;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public static class YamlLoader
{
    private static readonly IDeserializer DefaultDeserializer =  new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    /// <summary>
    /// YAMLファイルを読み込み、指定型でデシリアライズします。
    /// </summary>
    public static T Load<T>(string filePath)
    {
        using var reader = new StreamReader(filePath);
        return DefaultDeserializer.Deserialize<T>(reader);
    }

    /// <summary>
    /// カメラの内蔵パラメータ文字列をパースします。
    /// </summary>
    public static float[] ParseIntrinsics(string param)
    {
        return param.Trim('[', ']').Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(float.Parse).ToArray();
    }
}

using System.IO;
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
}

using System.Text.Json;
using Vitriol.Shared.Maps;

namespace Vitriol.Engine.Maps;

public static class MapSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ToJson(MapDto map)
        => JsonSerializer.Serialize(map, Options);

    public static MapDto FromJson(string json)
        => JsonSerializer.Deserialize<MapDto>(json, Options)
           ?? throw new InvalidOperationException("Failed to deserialize MapDto (null).");

    public static void SaveToFile(string path, MapDto map)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToJson(map));
    }

    public static MapDto LoadFromFile(string path)
        => FromJson(File.ReadAllText(path));
}

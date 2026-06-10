using Vitriol.Shared.Maps;

namespace Vitriol.Shared.Validation;

public static class MapValidator
{
    public static IReadOnlyList<ValidationError> Validate(MapDto map)
    {
        var errors = new List<ValidationError>();

        if (map.FormatVersion <= 0)
            errors.Add(new("FormatVersion", "FormatVersion must be a positive integer."));

        if (string.IsNullOrWhiteSpace(map.MapId))
            errors.Add(new("MapId", "MapId is required."));

        if (map.Width <= 0 || map.Height <= 0)
            errors.Add(new("Size", "Width and Height must be positive."));

        if (map.TileSize <= 0)
            errors.Add(new("TileSize", "TileSize must be positive."));

        foreach (var layer in map.Layers)
        {
            if (layer.Tiles is null)
            {
                errors.Add(new($"Layer:{layer.LayerId}", "Tiles array is missing."));
                continue;
            }

            var expected = map.Width * map.Height;
            if (layer.Tiles.Length != expected)
                errors.Add(new($"Layer:{layer.LayerId}", $"Tiles length must be Width*Height ({expected})."));
        }

        return errors;
    }
}

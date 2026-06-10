using NUnit.Framework;
using Vitriol.Engine.Maps;
using Vitriol.Shared.Maps;
using Vitriol.Shared.Validation;

namespace Vitriol.Tests;

public class MapFormatTests
{
    [Test]
    public void Map_json_round_trip_preserves_dimensions_and_tiles()
    {
        var w = 8;
        var h = 6;

        var tiles = new int[w * h];
        for (int i = 0; i < tiles.Length; i++) tiles[i] = i % 3;

        var map = new MapDto(
            FormatVersion: MapFormat.CurrentVersion,
            MapId: "round_trip",
            Name: "Round Trip",
            Width: w,
            Height: h,
            TileSize: 32,
            Tilesets: new List<TilesetRefDto>(),
            Layers: new List<TileLayerDto>
            {
                new("bg", "Background", LayerKind.Background, true, tiles)
            }
        );

        var errors = MapValidator.Validate(map);
        Assert.That(errors, Is.Empty);

        var json = MapSerializer.ToJson(map);
        var loaded = MapSerializer.FromJson(json);

        Assert.That(loaded.Width, Is.EqualTo(map.Width));
        Assert.That(loaded.Height, Is.EqualTo(map.Height));
        Assert.That(loaded.Layers.Count, Is.EqualTo(1));
        Assert.That(loaded.Layers[0].Tiles, Is.EqualTo(map.Layers[0].Tiles));
    }
}

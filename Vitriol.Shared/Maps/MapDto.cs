using System.Collections.Generic;

namespace Vitriol.Shared.Maps;

public sealed record MapDto(
    int FormatVersion,
    string MapId,
    string Name,
    int Width,
    int Height,
    int TileSize,
    IReadOnlyList<TilesetRefDto> Tilesets,
    IReadOnlyList<TileLayerDto> Layers
)
{
    public IReadOnlyList<MapEventDto> Events { get; init; }
        = System.Array.Empty<MapEventDto>();
}

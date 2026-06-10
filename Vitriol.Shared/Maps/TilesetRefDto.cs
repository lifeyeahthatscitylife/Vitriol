namespace Vitriol.Shared.Maps;

public sealed record TilesetRefDto(
    string TilesetId,
    string ImagePath,      // relative to project root, e.g. "assets/tilesets/kanto.png"
    int TileWidth,
    int TileHeight,
    int Margin,
    int Spacing
);

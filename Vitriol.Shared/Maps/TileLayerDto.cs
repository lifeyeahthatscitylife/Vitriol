namespace Vitriol.Shared.Maps;

public sealed record TileLayerDto(
    string LayerId,
    string Name,
    LayerKind Kind,
    bool Visible,
    int[] Tiles // length = Width*Height, tile index (or -1 for empty)
);

public enum LayerKind
{
    Background = 0,
    Collision = 1,
    Objects = 2,
    Overlay = 3
}

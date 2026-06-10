namespace Vitriol.Shared.Maps;

public sealed record MapEventDto(
    string EventId,
    string Name,
    string Type,
    int X,
    int Y,
    Dictionary<string, string>? Parameters = null,
    IReadOnlyList<EventCommandDto>? Commands = null
);

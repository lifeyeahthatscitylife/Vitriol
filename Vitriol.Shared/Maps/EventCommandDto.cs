namespace Vitriol.Shared.Maps;

public sealed record EventCommandDto(
    string Command,
    Dictionary<string, string> Args
);

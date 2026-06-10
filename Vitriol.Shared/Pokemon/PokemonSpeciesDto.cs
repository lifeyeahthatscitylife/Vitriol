namespace Vitriol.Shared.Pokemon;

public sealed record PokemonSpeciesDto(
    long SpeciesId,
    string Name,
    string PrimaryType,
    string? SecondaryType,
    int BaseHP,
    int BaseAttack,
    int BaseDefense,
    int BaseSpAttack,
    int BaseSpDefense,
    int BaseSpeed
);

using Vitriol.Shared.Pokemon;

namespace Vitriol.Shared.Validation;

public static class SpeciesValidator
{
    public static IReadOnlyList<ValidationError> Validate(PokemonSpeciesDto dto)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(dto.Name))
            errors.Add(new("Name", "Name is required."));

        if (string.IsNullOrWhiteSpace(dto.PrimaryType))
            errors.Add(new("PrimaryType", "Primary type is required."));

        if (!string.IsNullOrWhiteSpace(dto.SecondaryType) &&
            string.Equals(dto.PrimaryType, dto.SecondaryType, StringComparison.OrdinalIgnoreCase))
            errors.Add(new("SecondaryType", "Secondary type cannot be the same as primary type."));

        int[] stats = [dto.BaseHP, dto.BaseAttack, dto.BaseDefense, dto.BaseSpAttack, dto.BaseSpDefense, dto.BaseSpeed];
        if (stats.Any(s => s < 1 || s > 255))
            errors.Add(new("BaseStats", "Each base stat must be between 1 and 255."));

        return errors;
    }
}

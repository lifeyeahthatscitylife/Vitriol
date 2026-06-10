using Microsoft.Data.Sqlite;
using Vitriol.Shared.Pokemon;

namespace Vitriol.Data.Repositories;

public sealed class PokemonSpeciesRepository
{
    public long Insert(SqliteConnection conn, PokemonSpeciesDto dto)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO pokemon_species
              (name, primary_type, secondary_type, base_hp, base_atk, base_def, base_spa, base_spd, base_spe)
            VALUES
              ($name, $pt, $st, $hp, $atk, $def, $spa, $spd, $spe);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("$name", dto.Name);
        cmd.Parameters.AddWithValue("$pt", dto.PrimaryType);
        cmd.Parameters.AddWithValue("$st", (object?)dto.SecondaryType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hp", dto.BaseHP);
        cmd.Parameters.AddWithValue("$atk", dto.BaseAttack);
        cmd.Parameters.AddWithValue("$def", dto.BaseDefense);
        cmd.Parameters.AddWithValue("$spa", dto.BaseSpAttack);
        cmd.Parameters.AddWithValue("$spd", dto.BaseSpDefense);
        cmd.Parameters.AddWithValue("$spe", dto.BaseSpeed);

        return (long)cmd.ExecuteScalar()!;
    }

    public PokemonSpeciesDto? GetByName(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT species_id, name, primary_type, secondary_type, base_hp, base_atk, base_def, base_spa, base_spd, base_spe
            FROM pokemon_species
            WHERE name = $name
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$name", name);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new PokemonSpeciesDto(
            SpeciesId: r.GetInt64(0),
            Name: r.GetString(1),
            PrimaryType: r.GetString(2),
            SecondaryType: r.IsDBNull(3) ? null : r.GetString(3),
            BaseHP: r.GetInt32(4),
            BaseAttack: r.GetInt32(5),
            BaseDefense: r.GetInt32(6),
            BaseSpAttack: r.GetInt32(7),
            BaseSpDefense: r.GetInt32(8),
            BaseSpeed: r.GetInt32(9)
        );
    }
}

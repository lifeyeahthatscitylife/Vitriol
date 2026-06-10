using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Vitriol.Data.Migrations;
using Vitriol.Data.Repositories;
using Vitriol.Shared.Pokemon;
using Vitriol.Shared.Validation;

namespace Vitriol.Tests;

public class DatabaseSmokeTests
{
    [Test]
    public void Migration_creates_species_table_and_allows_insert_and_query()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        new MigrationRunner().EnsureMigrated(conn);

        var repo = new PokemonSpeciesRepository();
        var bulbasaur = new PokemonSpeciesDto(
            SpeciesId: 0,
            Name: "Bulbasaur",
            PrimaryType: "Grass",
            SecondaryType: "Poison",
            BaseHP: 45,
            BaseAttack: 49,
            BaseDefense: 49,
            BaseSpAttack: 65,
            BaseSpDefense: 65,
            BaseSpeed: 45
        );

        var errors = SpeciesValidator.Validate(bulbasaur);
        Assert.That(errors, Is.Empty);

        var id = repo.Insert(conn, bulbasaur);
        Assert.That(id, Is.GreaterThan(0));

        var loaded = repo.GetByName(conn, "Bulbasaur");
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.PrimaryType, Is.EqualTo("Grass"));
        Assert.That(loaded.SecondaryType, Is.EqualTo("Poison"));
    }

    [Test]
    public void Validator_rejects_duplicate_types()
    {
        var dto = new PokemonSpeciesDto(
            SpeciesId: 0,
            Name: "Testmon",
            PrimaryType: "Fire",
            SecondaryType: "Fire",
            BaseHP: 50, BaseAttack: 50, BaseDefense: 50, BaseSpAttack: 50, BaseSpDefense: 50, BaseSpeed: 50
        );

        var errors = SpeciesValidator.Validate(dto);
        Assert.That(errors.Any(e => e.Field == "SecondaryType"), Is.True);
    }
}

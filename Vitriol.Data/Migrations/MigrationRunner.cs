using Microsoft.Data.Sqlite;

namespace Vitriol.Data.Migrations;

public sealed class MigrationRunner
{
    public void EnsureMigrated(SqliteConnection conn)
    {
        // 1) migration history table
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS __migrations (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_utc TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        // 2) Apply migrations in order
        ApplyIfMissing(conn, 1, "001_init_species", """
            CREATE TABLE IF NOT EXISTS pokemon_species (
                species_id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                primary_type TEXT NOT NULL,
                secondary_type TEXT NULL,
                base_hp INTEGER NOT NULL,
                base_atk INTEGER NOT NULL,
                base_def INTEGER NOT NULL,
                base_spa INTEGER NOT NULL,
                base_spd INTEGER NOT NULL,
                base_spe INTEGER NOT NULL
            );
        """);
    }

    private static void ApplyIfMissing(SqliteConnection conn, long id, string name, string sql)
    {
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT 1 FROM __migrations WHERE id = $id LIMIT 1;";
        check.Parameters.AddWithValue("$id", id);

        var exists = check.ExecuteScalar() is not null;
        if (exists) return;

        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText =
                "INSERT INTO __migrations(id, name, applied_utc) VALUES($id, $name, $utc);";
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$name", name);
            insert.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }
}

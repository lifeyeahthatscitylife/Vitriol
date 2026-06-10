using Microsoft.Data.Sqlite;

namespace Vitriol.Data.Db;

public sealed class SqliteConnectionFactory(string connectionString)
{
    public SqliteConnection Create()
        => new(connectionString);

    public static SqliteConnectionFactory ForFile(string dbFilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbFilePath)!);
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        return new(cs);
    }
}

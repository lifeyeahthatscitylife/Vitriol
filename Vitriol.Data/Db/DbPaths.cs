namespace Vitriol.Data.Db;

public static class DbPaths
{
    public static string DefaultDbFile(string projectRoot)
        => Path.Combine(projectRoot, "data", "vitriol.db");
}

namespace SmartGym.Data.Repositories;

/// <summary>Base común de repositorios: cada operación abre su propia conexión.</summary>
public abstract class RepositoryBase
{
    protected string DbPath { get; }

    protected RepositoryBase(string dbPath)
    {
        DbPath = dbPath;
    }
}
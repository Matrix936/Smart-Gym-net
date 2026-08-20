using SmartGym.Data.Db;

namespace SmartGym.Tests.Data;

/// <summary>
/// Una BD SQLite temporal por clase de test. ConnectionFactory usa pooling,
/// así que no se borra el archivo en Dispose (el OS limpia la carpeta temp).
/// </summary>
public sealed class DataTestFixture : IDisposable
{
    public string DbPath { get; }

    public DataTestFixture()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"smart_gym_test_{Guid.NewGuid():N}.db");
        DbInitializer.Initialize(DbPath);
    }

    public void Dispose()
    {
    }
}

[CollectionDefinition("data")]
public sealed class DataTestCollection : ICollectionFixture<DataTestFixture>
{
}
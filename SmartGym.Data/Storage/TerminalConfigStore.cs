using SmartGym.Core.Services;

namespace SmartGym.Data.Storage;

/// <summary>
/// Guarda la sede del equipo en {baseDir}/terminal_config.txt (mismo directorio
/// y mismo patrón que sesion.token — SesionStore). Tolerante a archivo
/// ausente/corrupto: devuelve null y el Kiosco vuelve a pedir la sede.
/// </summary>
public sealed class TerminalConfigStore : ITerminalConfigStore
{
    private readonly string _path;

    public TerminalConfigStore(string baseDir)
    {
        _path = Path.Combine(baseDir, "terminal_config.txt");
    }

    public Task<long?> ObtenerIdSedeAsync()
    {
        if (!File.Exists(_path))
        {
            return Task.FromResult<long?>(null);
        }

        try
        {
            var texto = File.ReadAllText(_path).Trim();
            long? resultado = long.TryParse(texto, out var idSede) ? idSede : null;
            return Task.FromResult(resultado);
        }
        catch
        {
            return Task.FromResult<long?>(null);
        }
    }

    public Task GuardarIdSedeAsync(long idSede)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, idSede.ToString());
        return Task.CompletedTask;
    }
}

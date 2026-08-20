using SmartGym.Core.Services;

namespace SmartGym.Data.Storage;

/// <summary>
/// Guarda el token de sesión activo en {baseDir}/sesion.token (mismo directorio
/// que la DB y los logos). Tolerante a archivo ausente/corrupto: devuelve null.
/// </summary>
public sealed class SesionStore : ISesionStore
{
    private readonly string _path;

    public SesionStore(string baseDir)
    {
        _path = Path.Combine(baseDir, "sesion.token");
    }

    public Task GuardarAsync(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, (token ?? "").Trim());
        return Task.CompletedTask;
    }

    public Task<string?> ObtenerTokenAsync()
    {
        if (!File.Exists(_path))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var token = File.ReadAllText(_path).Trim();
            return Task.FromResult<string?>(string.IsNullOrEmpty(token) ? null : token);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task LimpiarAsync()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // El siguiente arranque simplemente no restaurará sesión.
        }
        return Task.CompletedTask;
    }
}
using SmartGym.Core.Errors;
using SmartGym.Core.Services;

namespace SmartGym.Data.Storage;

/// <summary>
/// Guarda el logo en {baseDir}/logos/logo.{ext}. Cumple las métricas del setup:
/// archivo determinista, tamaño máx 512 KB, limpieza de huérfanos al cambiar formato.
/// </summary>
public sealed class LogoStorage : ILogoStorage
{
    private const long MaxBytes = 512 * 1024;
    private static readonly string[] ExtensionesPermitidas = [".png", ".svg", ".jpg", ".jpeg", ".webp"];

    private readonly string _baseDir;

    public LogoStorage(string baseDir)
    {
        _baseDir = baseDir;
    }

    public string Guardar(byte[] bytes, string extension)
    {
        if (!ExtensionesPermitidas.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw BusinessException.Validation("Tipo de archivo de logo no permitido", "logo_mime_no_permitido");
        }

        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            throw BusinessException.Validation("El archivo de logo excede el tamaño máximo (512 KB)", "logo_tamanio_excesivo");
        }

        var dir = Path.Combine(_baseDir, "logos");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"logo{extension.ToLowerInvariant()}");
        File.WriteAllBytes(path, bytes);

        return Path.Combine("logos", $"logo{extension.ToLowerInvariant()}").Replace('\\', '/');
    }

    public void EliminarHuérfanos(string extension)
    {
        var dir = Path.Combine(_baseDir, "logos");
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(dir, "logo.*"))
        {
            if (!file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }

    public string? LeerDataUrl()
    {
        var dir = Path.Combine(_baseDir, "logos");
        if (!Directory.Exists(dir))
        {
            return null;
        }

        foreach (var ext in ExtensionesPermitidas)
        {
            var path = Path.Combine(dir, $"logo{ext}");
            if (!File.Exists(path))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            return $"data:{MimeDe(ext)};base64,{Convert.ToBase64String(bytes)}";
        }

        return null;
    }

    public void Eliminar()
    {
        var dir = Path.Combine(_baseDir, "logos");
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(dir, "logo.*"))
        {
            File.Delete(file);
        }
    }

    private static string MimeDe(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}
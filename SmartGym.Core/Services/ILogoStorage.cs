namespace SmartGym.Core.Services;

/// <summary>
/// Persistencia del logo (offline, ruta relativa en DB). Guarda un archivo
/// determinista ("logos/logo.{ext}") y permite limpiar huérfanos de un
/// formato anterior al cambiar de extensión (setup.rs de la versión Rust).
/// </summary>
public interface ILogoStorage
{
    /// <summary>Guarda el logo. Devuelve la ruta relativa a almacenar en DB.</summary>
    /// <exception cref="SmartGym.Core.Errors.BusinessException">Validation: mime no permitido o tamaño excesivo.</exception>
    string Guardar(byte[] bytes, string extension);

    /// <summary>Devuelve el logo actual como data URL para mostrar en UI, o null si no hay logo.</summary>
    string? LeerDataUrl();

    /// <summary>Borra archivos "logo.*" que no correspondan a la extensión actual.</summary>
    void EliminarHuérfanos(string extension);
}
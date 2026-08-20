namespace SmartGym.Core.Services;

/// <summary>
/// Persistencia del token de sesión activo para sobrevivir a recargas (Ctrl+R)
/// y reinicios de la app. Una sola sesión activa: el último login gana.
/// </summary>
public interface ISesionStore
{
    Task GuardarAsync(string token);
    Task<string?> ObtenerTokenAsync();
    Task LimpiarAsync();
}
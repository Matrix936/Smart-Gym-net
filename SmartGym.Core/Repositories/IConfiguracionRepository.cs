namespace SmartGym.Core.Repositories;

/// <summary>configuracion_general — pares clave/valor sincronizables.</summary>
public interface IConfiguracionRepository
{
    Task<string?> GetAsync(string clave, CancellationToken ct = default);
    Task SetAsync(string clave, string? valor, CancellationToken ct = default);
}
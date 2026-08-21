using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

/// <summary>Datos de alta de socio (id_sede_registro se resuelve server-side).</summary>
public sealed class CrearSocioDatos
{
    public required string Nombre { get; init; }
    public string? ApellidoPaterno { get; init; }
    public string? ApellidoMaterno { get; init; }
    public string? Email { get; init; }
    public string? Telefono { get; init; }
    public string? FechaNacimiento { get; init; }
    public string? FotoPath { get; init; }
    public string? ContactoEmergenciaNombre { get; init; }
    public string? ContactoEmergenciaTelefono { get; init; }
}

public sealed class ActualizarSocioDatos
{
    public required string IdSocio { get; init; }
    public required string Nombre { get; init; }
    public string? ApellidoPaterno { get; init; }
    public string? ApellidoMaterno { get; init; }
    public string? Email { get; init; }
    public string? Telefono { get; init; }
    public string? FechaNacimiento { get; init; }
    public string? FotoPath { get; init; }
    public string? ContactoEmergenciaNombre { get; init; }
    public string? ContactoEmergenciaTelefono { get; init; }
}

/// <summary>
/// Módulo socios (members.rs): CRUD con resolución de id_sede (la sesión local
/// gana sobre el id_sede del frontend), historial de estado y auditoría.
/// </summary>
public interface ISociosService
{
    Task<Socio> CrearSocioAsync(string token, CrearSocioDatos datos, long? idSedeFrontend = null, CancellationToken ct = default);
    Task<Socio> ObtenerSocioAsync(string token, string idSocio, CancellationToken ct = default);
    Task<PagedResult<Socio>> BuscarAsync(string token, string? query = null, int pagina = 1, int tamanoPagina = TamanosPagina.Default, CancellationToken ct = default);
    Task<Socio> ActualizarSocioAsync(string token, ActualizarSocioDatos datos, CancellationToken ct = default);
    Task CambiarEstadoAsync(string token, string idSocio, string estadoNuevo, string? motivo = null, CancellationToken ct = default);
    Task EliminarSocioAsync(string token, string idSocio, CancellationToken ct = default);
}
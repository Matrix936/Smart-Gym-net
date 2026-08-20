using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

/// <summary>
/// Módulo memberships (memberships.rs): venta (alta/renovación), congelamiento y
/// cancelación. La venta exige caja abierta y corre en una transacción única;
/// la cancelación exige reautorización con clave.
/// </summary>
public interface IMembresiasService
{
    Task<Membresia> VenderAsync(
        string token,
        string idSocio,
        long idPlan,
        string metodoPago,
        long montoRecibidoCentavos,
        long? idSedeFrontend = null,
        CancellationToken ct = default);

    Task<Membresia> CongelarAsync(
        string token,
        string idMembresia,
        string fechaInicio,
        string fechaFin,
        string? motivo = null,
        CancellationToken ct = default);

    Task<Membresia> CancelarAsync(
        string token,
        string idMembresia,
        string password,
        string? motivo = null,
        CancellationToken ct = default);
}
using SmartGym.Core.Common;

namespace SmartGym.Core.Repositories;

/// <summary>
/// Consultas de solo lectura exclusivas del Dashboard (/): afluencia de accesos
/// y candidatos a recordatorio de membresía. El resumen monetario lo provee
/// IFinanzasRepository (misma fuente de dinero que /finanzas).
/// </summary>
public interface IDashboardRepository
{
    /// <summary>
    /// Timestamps ISO UTC de accesos CONCEDIDOS desde desdeIso (inclusive),
    /// opcionalmente filtrados por sede (null = todas). La agregación por hora
    /// local la hace el servicio (el timestamp vive en UTC).
    /// </summary>
    Task<IReadOnlyList<string>> ObtenerAccesosConcedidosAsync(
        long? idSede, string desdeIso, CancellationToken ct = default);

    /// <summary>
    /// Membresías ACTIVAS (estado crudo 'activa') con nombre y teléfono del
    /// socio — materia prima para clasificar por vencimiento con
    /// MembresiaEstadoCalculator en el servicio.
    /// </summary>
    Task<IReadOnlyList<(string IdSocio, string NombreSocio, string Telefono, string FechaFin)>> ObtenerActivasConSocioAsync(
        long? idSede, CancellationToken ct = default);

    /// <summary>
    /// Cuentas por cobrar VENCIDAS (pendiente/parcial con fecha_vencimiento
    /// pasada) con nombre y teléfono del socio - materia prima para el panel
    /// de cobranza vencida del Dashboard.
    /// </summary>
    Task<IReadOnlyList<CobranzaVencidaDto>> ObtenerCobranzaVencidaConSocioAsync(
        long? idSede, CancellationToken ct = default);}

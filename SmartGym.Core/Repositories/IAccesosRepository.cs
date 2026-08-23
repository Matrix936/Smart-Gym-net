using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>
/// Control de acceso (access.rs / checklist 03). El registro de acceso es
/// atómico: evalúa socio + membresía, alterna tipo por día, actualiza
/// socios.fecha_ultimo_acceso solo si concedido e inserta la bitácora.
/// </summary>
public interface IAccesosRepository
{
    /// <summary>Acceso Kiosco (sin sesión). Valida socio, membresía y dispositivo; registra bitácora.</summary>
    Task<AccesoResult> RegistrarKioskoAsync(
        string idSocio,
        long idSede,
        long? idDispositivo,
        CancellationToken ct = default);

    /// <summary>Acceso manual (sesión + permiso acceso.forzar_entrada_manual). Método registrado: manual.</summary>
    Task<AccesoResult> RegistrarManualAsync(
        string idSocio,
        long idSede,
        long? idDispositivo,
        CancellationToken ct = default);

    /// <summary>Lee un registro de la bitácora (para aserciones y la pantalla de bitácora).</summary>
    Task<AccesoBitacora?> GetByIdAsync(string idAcceso, CancellationToken ct = default);

    /// <summary>
    /// Historial paginado de intentos de acceso de una sede con el socio
    /// resuelto. Orden descendente por timestamp.
    /// </summary>
    Task<PagedResult<AccesoHistorialDto>> BuscarAsync(
        long? idSede,
        AccesoHistorialFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        CancellationToken ct = default);
}
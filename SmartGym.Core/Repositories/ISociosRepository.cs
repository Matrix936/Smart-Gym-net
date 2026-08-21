using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>
/// CRUD de socios (members.rs). Alta y cambio de estado son atómicas e
/// incluyen su bitácora de auditoría en la misma transacción.
/// </summary>
public interface ISociosRepository
{
    /// <summary>INSERT normal (single-write). El id_socio (UUID) lo pone el dominio.</summary>
    Task InsertAsync(Socio socio, CancellationToken ct = default);

    /// <summary>Alta atómica: socio + bitacora en una transacción.</summary>
    Task CrearConBitacoraAsync(Socio socio, BitacoraAuditoria bitacora, CancellationToken ct = default);

    Task<Socio?> GetByIdAsync(string idSocio, CancellationToken ct = default);
    Task<bool> ExistsAsync(string idSocio, CancellationToken ct = default);

    /// <summary>
    /// Búsqueda LIKE por nombre, email o teléfono, paginada. Query null → todos los no
    /// borrados. Estado null → sin filtro por estado; si viene valor debe ser uno de
    /// SocioEstados.Validos. tamanoPagina debe estar en TamanosPagina.Validos (10/25/50);
    /// cualquier otro valor lanza ArgumentException. pagina fuera de rango (más allá del
    /// total) devuelve Items vacío, no error.
    /// </summary>
    Task<PagedResult<Socio>> SearchAsync(string? query = null, string? estado = null, int pagina = 1, int tamanoPagina = TamanosPagina.Default, CancellationToken ct = default);

    /// <summary>Actualiza campos editables preservando id_socio e id_sede_registro.</summary>
    Task UpdateAsync(Socio socio, CancellationToken ct = default);

    /// <summary>Actualización atómica: UPDATE + bitácora de auditoría en la misma transacción.</summary>
    Task ActualizarConBitacoraAsync(Socio socio, BitacoraAuditoria bitacora, CancellationToken ct = default);

    /// <summary>Todo DELETE soft delete (deleted_at). No afecta si ya está borrado.</summary>
    Task SoftDeleteAsync(string idSocio, CancellationToken ct = default);

    /// <summary>Historial de cambios de estado de un socio (ordenado por created_at).</summary>
    Task<IReadOnlyList<SocioHistorialEstado>> HistorialDeAsync(string idSocio, CancellationToken ct = default);

    /// <summary>Cambio de estado atómico: estado + historial + bitacora en una transacción.</summary>
    Task CambiarEstadoConBitacoraAsync(
        string idSocio,
        string estadoNuevo,
        SocioHistorialEstado historial,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default);
}
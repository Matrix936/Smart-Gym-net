using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IMaquinariaRepository
{
    /// <summary>Máquina activa y no eliminada, o null.</summary>
    Task<Maquina?> GetByIdAsync(string idMaquina, CancellationToken ct = default);

    /// <summary>Máquina sin importar es_activo (para editar/desactivar/activar).</summary>
    Task<Maquina?> GetByIdCualquierEstadoAsync(string idMaquina, CancellationToken ct = default);

    Task InsertAsync(Maquina maquina, CancellationToken ct = default);

    Task UpdateAsync(Maquina maquina, CancellationToken ct = default);

    /// <summary>Búsqueda paginada por sede, con filtro opcional de nombre y estado.</summary>
    Task<PagedResult<Maquina>> SearchAsync(
        long idSede,
        string? nombre,
        string? estado,
        int pagina,
        int tamanoPagina,
        bool? esActivo = null,
        CancellationToken ct = default);

    /// <summary>Soft-desactivación: es_activo=0, la fila permanece (bitácora intacta).</summary>
    Task DesactivarAsync(string idMaquina, string updatedAt, CancellationToken ct = default);

    Task ActivarAsync(string idMaquina, string updatedAt, CancellationToken ct = default);
}

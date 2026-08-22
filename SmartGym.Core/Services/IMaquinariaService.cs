using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

public interface IMaquinariaService
{
    Task<PagedResult<Maquina>> BuscarAsync(
        string token,
        string? nombre = null,
        string? estado = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        bool? esActivo = null,
        long? idSedeFrontend = null,
        CancellationToken ct = default);

    Task<Maquina> CrearAsync(
        string token,
        string nombre,
        string? descripcion,
        string estado,
        string? notas,
        long? idSedeFrontend = null,
        CancellationToken ct = default);

    Task<Maquina> EditarAsync(
        string token,
        string idMaquina,
        string nombre,
        string? descripcion,
        string? notas,
        CancellationToken ct = default);

    /// <summary>Cambio de estado operativo (funcionando/mantenimiento/fuera de servicio).</summary>
    Task<Maquina> CambiarEstadoAsync(string token, string idMaquina, string estadoNuevo, CancellationToken ct = default);

    /// <summary>Baja lógica: deja de listarse pero la fila permanece (bitácora intacta).</summary>
    Task DesactivarAsync(string token, string idMaquina, CancellationToken ct = default);

    Task ActivarAsync(string token, string idMaquina, CancellationToken ct = default);
}

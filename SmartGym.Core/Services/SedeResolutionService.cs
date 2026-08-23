using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class SedeResolutionService : ISedeResolutionService
{
    private readonly ISedesRepository _sedes;

    public SedeResolutionService(ISedesRepository sedes)
    {
        _sedes = sedes;
    }

    public async Task<long> ResolverIdSedeAsync(SessionInfo info, long? idSedeFrontend, CancellationToken ct = default)
    {
        if (info.IdSede is not null)
        {
            return info.IdSede.Value;
        }

        if (idSedeFrontend is null)
        {
            throw BusinessException.Validation("Se requiere una sede para esta operación", "sede_requerida");
        }

        var sede = await _sedes.GetByIdAsync(idSedeFrontend.Value, ct);
        if (sede is null || !sede.EsActiva)
        {
            throw BusinessException.Validation("La sede indicada no existe o no está activa", "sede_invalida");
        }

        return idSedeFrontend.Value;
    }

    public async Task<long?> ResolverIdSedeOpcionalAsync(SessionInfo info, long? idSedeFrontend, CancellationToken ct = default)
    {
        if (info.IdSede is not null)
        {
            return info.IdSede.Value;
        }

        if (idSedeFrontend is null)
        {
            return null; // "todas las sedes" — solo válido en listados.
        }

        await ValidarSedeAsync(idSedeFrontend.Value, ct);
        return idSedeFrontend;
    }

    private async Task ValidarSedeAsync(long idSede, CancellationToken ct)
    {
        var sede = await _sedes.GetByIdAsync(idSede, ct);
        if (sede is null || !sede.EsActiva)
        {
            throw BusinessException.Validation("La sede indicada no existe o no está activa", "sede_invalida");
        }
    }
}

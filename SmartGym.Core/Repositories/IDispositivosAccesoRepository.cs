using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>dispositivos_acceso — catálogo por sede para validez al registrar accesos.</summary>
public interface IDispositivosAccesoRepository
{
    /// <summary>¿El dispositivo existe, está activo y pertenece a la sede? (regla de kiosko/dispositivo inválido).</summary>
    Task<bool> ExisteActivoEnSedeAsync(long idDispositivo, long idSede, CancellationToken ct = default);

    /// <summary>INSERT directo (usado por tests y setup de dispositivos).</summary>
    Task<long> InsertAsync(DispositivoAcceso dispositivo, CancellationToken ct = default);
}
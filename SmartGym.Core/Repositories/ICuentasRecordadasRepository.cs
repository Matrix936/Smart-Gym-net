using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>cuentas_recordadas_local — autocomplete del login, tabla local-only.</summary>
public interface ICuentasRecordadasRepository
{
    Task<IReadOnlyList<CuentaRecordadaLocal>> GetAllAsync(CancellationToken ct = default);
    Task<CuentaRecordadaLocal?> GetByEmailAsync(string email, CancellationToken ct = default);
    /// <summary>Guarda la cuenta (upsert: no duplica si ya existe).</summary>
    Task UpsertAsync(CuentaRecordadaLocal cuenta, CancellationToken ct = default);
}
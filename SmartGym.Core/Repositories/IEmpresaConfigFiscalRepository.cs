using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>empresa_config_fiscal — fila única con los datos del negocio.</summary>
public interface IEmpresaConfigFiscalRepository
{
    Task<EmpresaConfigFiscal?> GetAsync(CancellationToken ct = default);
    /// <summary>Insertar si no existe, actualizar si ya existe (fila única).</summary>
    Task SaveAsync(EmpresaConfigFiscal empresa, CancellationToken ct = default);
    Task SetLogoPathAsync(string? logoPath, CancellationToken ct = default);
}
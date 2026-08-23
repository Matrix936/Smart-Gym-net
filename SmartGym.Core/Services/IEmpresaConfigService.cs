using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

public interface IEmpresaConfigService
{
    /// <summary>Config de empresa + logo actual (data URL para preview en UI).</summary>
    Task<(EmpresaConfigFiscal Empresa, string? LogoDataUrl)> ObtenerAsync(string token, CancellationToken ct = default);

    /// <summary>Guarda los datos editables de la empresa (fila única creada por el SetupWizard).</summary>
    Task<EmpresaConfigFiscal> ActualizarDatosAsync(string token, string nombreComercial, string? telefono,
        string? direccion, string? codigoPostal, string? razonSocial, string? rfc, string? regimenFiscal,
        CancellationToken ct = default);

    /// <summary>Guarda/sube un nuevo logo (reemplaza al anterior, limpia huérfanos).</summary>
    Task GuardarLogoAsync(string token, byte[] bytes, string mime, CancellationToken ct = default);

    /// <summary>Quita el logo actual.</summary>
    Task QuitarLogoAsync(string token, CancellationToken ct = default);

    /// <summary>Preferencia de impresora (clave/valor global; sin flujo de impresión todavía).</summary>
    Task GuardarImpresoraAsync(string token, string nombreImpresora, CancellationToken ct = default);

    Task<string?> ObtenerImpresoraAsync(string token, CancellationToken ct = default);
}

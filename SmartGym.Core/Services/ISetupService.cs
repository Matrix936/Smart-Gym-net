using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

public enum SetupEstadoResultado
{
    Pendiente,
    Completa,
}

public sealed class SetupEstado
{
    public required SetupEstadoResultado Estado { get; init; }
    public bool Completado => Estado == SetupEstadoResultado.Completa;
}

/// <summary>Datos de completar_configuracion_inicial (setup.rs).</summary>
public sealed class SetupDatos
{
    public required string NombreComercial { get; init; }
    public required string Telefono { get; init; }
    public required string Direccion { get; init; }
    public required string CodigoPostal { get; init; }
    public string? RazonSocial { get; init; }
    public string? Rfc { get; init; }
    public string? RegimenFiscal { get; init; }

    public string? NombreAdmin { get; init; }
    public string? ApellidoPaternoAdmin { get; init; }
    public string? ApellidoMaternoAdmin { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }

    public byte[]? LogoBytes { get; init; }
    public string? LogoMime { get; init; }
}

/// <summary>
/// Módulo setup: es el primer flujo que usa la app. Crea el superadmin,
/// los datos fiscales y (opcionalmente) el logo. No se deshace.
/// </summary>
public interface ISetupService
{
    Task<SetupEstado> VerificarEstadoAsync(CancellationToken ct = default);
    Task CompletarConfiguracionInicialAsync(SetupDatos datos, CancellationToken ct = default);
    Task<EmpresaConfigFiscal> ObtenerDatosEmpresaAsync(CancellationToken ct = default);
    /// <summary>Guarda el logo (mime → extensión, determinista, limpia huérfanos).</summary>
    Task<string> GuardarLogoAsync(byte[] bytes, string mime, CancellationToken ct = default);
}
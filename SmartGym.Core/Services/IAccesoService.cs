using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

/// <summary>
/// Módulo de control de acceso (access.rs / checklist 03). El Kiosco no pasa por
/// validar_sesion (contexto sin sesión); el registro manual exige sesión +
/// permiso acceso.forzar_entrada_manual. La evaluación de membresía y la bitácora
/// viven en IAccesosRepository (transacción atómica).
/// </summary>
public interface IAccesoService
{
    /// <summary>Acceso Kiosco (sin sesión administrativa). Método: huella.</summary>
    Task<AccesoResult> RegistrarAccesoKioskoAsync(
        string idSocio,
        long idSede,
        long? idDispositivo = null,
        CancellationToken ct = default);

    /// <summary>Acceso manual (sesión + permiso). Método: manual.</summary>
    Task<AccesoResult> RegistrarAccesoManualAsync(
        string token,
        string idSocio,
        long idSede,
        long? idDispositivo = null,
        CancellationToken ct = default);
}
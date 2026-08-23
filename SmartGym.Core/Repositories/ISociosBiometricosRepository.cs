using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>
/// socios_biometricos — local-only. Template paths activos por sede para la
/// identificación 1:N (biometrics.rs: cargar_templates_por_sede_sync) y sync
/// del enrollment (re-enrolamiento del mismo dedo desactiva el anterior).
/// </summary>
public interface ISociosBiometricosRepository
{
    /// <summary>
    /// Templates activos (DISTINCT) de socios con membresía activa o congelada
    /// en la sede. Membresía vencida o inexistente  no aparece.
    /// </summary>
    Task<IReadOnlyList<string>> GetTemplatePathsBySedeAsync(long idSede, CancellationToken ct = default);

    /// <summary>
    /// TODOS los templates activos del sistema sin filtro de sede — para la
    /// detección de duplicados al enrolar (una huella pertenece a una persona,
    /// no a un lugar). Si idSocioExcluir se proporciona, excluye sus templates
    /// (re-enrolamiento no debe matchear contra sí mismo).
    /// </summary>
    Task<IReadOnlyList<string>> GetTodosLosTemplatesActivosAsync(string? idSocioExcluir = null, CancellationToken ct = default);

    /// <summary>Ids de socios con al menos un template de huella activo (indicador de huella en la lista de Miembros).</summary>
    Task<IReadOnlyList<string>> GetIdsConHuellaAsync(CancellationToken ct = default);

    /// <summary>Sync atómico del enrollment: desactiva es_activa=0 para el mismo dedo + inserta el nuevo.</summary>
    Task RegistrarTemplateAsync(string idSocio, string dedo, string templatePath, CancellationToken ct = default);
}
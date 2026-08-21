namespace SmartGym.Core.Services;

/// <summary>
/// Resuelve la sede efectiva de una operación (members.rs / memberships.rs /
/// pos.rs / cobranza.rs: "la sesión local gana sobre el id_sede del
/// frontend"). Único punto de esta regla — antes duplicada y divergente en
/// SociosService, CajaService, MembresiasService, PosService y CobranzaService
/// (auditoría SOLID hallazgo #2): la sede resuelta siempre debe existir y
/// estar activa.
/// </summary>
public interface ISedeResolutionService
{
    /// <summary>
    /// Si la sesión tiene id_sede, esa gana (SUPERADMIN con sede local no
    /// puede operar en otra sede). Si no, exige idSedeFrontend y valida que
    /// la sede exista y esté activa.
    /// </summary>
    /// <exception cref="Errors.BusinessException">
    /// "sede_requerida" si no hay sesión local ni override, "sede_invalida"
    /// si la sede indicada no existe o no está activa.
    /// </exception>
    Task<long> ResolverIdSedeAsync(SessionInfo info, long? idSedeFrontend, CancellationToken ct = default);
}

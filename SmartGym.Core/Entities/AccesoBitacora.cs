namespace SmartGym.Core.Entities;

public static class AccesoTipos
{
    public const string Entrada = "entrada";
    public const string Salida = "salida";
}

public static class AccesoMetodos
{
    public const string Huella = "huella";
    public const string Manual = "manual";
}

public static class AccesoEstados
{
    public const string Concedido = "concedido";
    public const string Denegado = "denegado";
}

/// <summary>Motivos de denegación de acceso (accesos_bitacora.motivo_denegacion). Ver AccesoDecisor.</summary>
public static class MotivosDenegacionAcceso
{
    public const string SocioBloqueado = "socio_bloqueado";
    public const string SocioInactivo = "socio_inactivo";
    public const string SocioSuspendido = "socio_suspendido";
    public const string MembresiaVencida = "membresia_vencida";
    public const string MembresiaCongelada = "membresia_congelada";
}

/// <summary>Resultado puro de una decisión de acceso (sin id_acceso ni datos del socio — eso lo arma el repositorio).</summary>
public sealed class AccesoDecision
{
    public required string Estado { get; init; }
    public string? MotivoDenegacion { get; init; }
}

/// <summary>
/// Decisión de acceso: socio + estado de membresía vigente → concedido/denegado.
/// Lógica pura, sin Dapper/SQLite, para poder testearse sin transacción ni base
/// de datos (extraída de AccesosRepository.RegistrarAsync tras encontrar que el
/// switch original dejaba caer SocioEstados.Suspendido en el default y lo
/// trataba como activo — bug real sin cobertura, ver auditoría SOLID).
/// </summary>
public static class AccesoDecisor
{
    /// <summary>
    /// estadoMembresiaVigente es el resultado de la consulta que ya filtra por
    /// estado IN ('activa','congelada') AND fecha_fin >= hoy, ORDER BY
    /// fecha_fin DESC LIMIT 1 — por construcción solo puede ser "activa",
    /// "congelada" o null (ninguna vigente encontrada → vencida).
    /// </summary>
    public static AccesoDecision Decidir(string estadoSocio, string? estadoMembresiaVigente)
    {
        switch (estadoSocio)
        {
            case SocioEstados.Bloqueado:
                return Denegado(MotivosDenegacionAcceso.SocioBloqueado);

            case SocioEstados.Inactivo:
                return Denegado(MotivosDenegacionAcceso.SocioInactivo);

            case SocioEstados.Suspendido:
                return Denegado(MotivosDenegacionAcceso.SocioSuspendido);

            case SocioEstados.Activo:
                return DecidirPorMembresia(estadoMembresiaVigente);

            default:
                // Estado de socio no contemplado: error explícito, nunca una
                // aprobación implícita por caer en un default silencioso.
                throw Errors.BusinessException.Validation(
                    $"Estado de socio no reconocido para decisión de acceso: '{estadoSocio}'",
                    "estado_socio_invalido");
        }
    }

    private static AccesoDecision DecidirPorMembresia(string? estadoMembresiaVigente) => estadoMembresiaVigente switch
    {
        MembresiaEstados.Congelada => Denegado(MotivosDenegacionAcceso.MembresiaCongelada),
        MembresiaEstados.Activa => new AccesoDecision { Estado = AccesoEstados.Concedido, MotivoDenegacion = null },
        _ => Denegado(MotivosDenegacionAcceso.MembresiaVencida),
    };

    private static AccesoDecision Denegado(string motivo) =>
        new() { Estado = AccesoEstados.Denegado, MotivoDenegacion = motivo };
}

/// <summary>accesos_bitacora — bitácora de entradas/salidas (id UUID v4).</summary>
public sealed class AccesoBitacora
{
    public string IdAcceso { get; set; } = string.Empty;
    public string? IdSocio { get; set; }
    public long IdSede { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Metodo { get; set; } = string.Empty;
    public long? IdDispositivo { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? MotivoDenegacion { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}

/// <summary>Datos mínimos del socio expuestos en kiosco cuando el acceso es concedido.</summary>
public sealed class SocioBasico
{
    public string IdSocio { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? FotoPath { get; set; }
}

/// <summary>Resultado de un registro de acceso (kiosko o manual).</summary>
public sealed class AccesoResult
{
    public string IdAcceso { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string? MotivoDenegacion { get; set; }
    public SocioBasico? Socio { get; set; }
}
namespace SmartGym.Core.Common;

/// <summary>Filtros del historial de accesos. Fechas ISO UTC inclusivas.</summary>
public sealed class AccesoHistorialFiltros
{
    public string? Desde { get; set; }
    public string? Hasta { get; set; }

    /// <summary>concedido | denegado.</summary>
    public string? Estado { get; set; }

    /// <summary>huella | manual.</summary>
    public string? Metodo { get; set; }

    /// <summary>Búsqueda por nombre del socio (normalizada sin acentos).</summary>
    public string? NombreSocio { get; set; }
}

/// <summary>Un intento de acceso (concedido o denegado) con el actor resuelto.</summary>
public sealed class AccesoHistorialDto
{
    public string Timestamp { get; set; } = string.Empty;
    public string? IdSocio { get; set; }
    public string? NombreSocio { get; set; }
    public string Tipo { get; set; } = string.Empty;       // entrada | salida
    public string Metodo { get; set; } = string.Empty;     // huella | manual
    public string Estado { get; set; } = string.Empty;     // concedido | denegado
    public string? MotivoDenegacion { get; set; }
}

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
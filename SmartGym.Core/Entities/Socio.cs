namespace SmartGym.Core.Entities;

/// <summary>socios — id UUID v4 (TEXT), id_sede_registro NOT NULL.</summary>
public sealed class Socio
{
    public string IdSocio { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string ApellidoPaterno { get; set; } = string.Empty;
    public string ApellidoMaterno { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string? FechaNacimiento { get; set; }
    public long IdSedeRegistro { get; set; }
    public string? FotoPath { get; set; }
    public string? ContactoEmergenciaNombre { get; set; }
    public string? ContactoEmergenciaTelefono { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? FechaUltimoAcceso { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Estados válidos de un socio (estado en socios / historial en socios_historial_estado).</summary>
public static class SocioEstados
{
    public const string Activo = "activo";
    public const string Inactivo = "inactivo";
    public const string Bloqueado = "bloqueado";
    public const string Suspendido = "suspendido";

    public static readonly IReadOnlyList<string> Validos = [Activo, Inactivo, Bloqueado, Suspendido];

    public static bool EsValido(string estado) =>
        Validos.Contains(estado, StringComparer.OrdinalIgnoreCase);
}
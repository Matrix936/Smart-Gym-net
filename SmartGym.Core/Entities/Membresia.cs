namespace SmartGym.Core.Entities;

/// <summary>Estados válidos de una membresía.</summary>
public static class MembresiaEstados
{
    public const string Activa = "activa";
    public const string Vencida = "vencida";
    public const string Congelada = "congelada";
    public const string Cancelada = "cancelada";

    public static bool EsValido(string estado) => estado is Activa or Vencida or Congelada or Cancelada;
}

/// <summary>membresias — id UUID v4. fecha_fin se calcula server-side desde el plan.</summary>
public sealed class Membresia
{
    public string IdMembresia { get; set; } = string.Empty;
    public string IdSocio { get; set; } = string.Empty;
    public long IdPlan { get; set; }
    public long IdSede { get; set; }
    public string FechaInicio { get; set; } = string.Empty;
    public string FechaFin { get; set; } = string.Empty;
    public string? FechaCancelacion { get; set; }
    public string Estado { get; set; } = MembresiaEstados.Activa;
    public long? IdVendedor { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
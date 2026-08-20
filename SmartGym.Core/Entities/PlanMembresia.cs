namespace SmartGym.Core.Entities;

/// <summary>planes_membresia — catálogo (INTEGER AUTOINCREMENT). Dinero INTEGER centavos.</summary>
public sealed class PlanMembresia
{
    public long IdPlan { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int DiasVigencia { get; set; }
    public int DiasCongelamientoMax { get; set; }
    public long PrecioCentavos { get; set; }
    public bool EsActivo { get; set; } = true;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}
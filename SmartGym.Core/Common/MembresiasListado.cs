namespace SmartGym.Core.Common;

/// <summary>Fila del listado paginado de membresías con socio/plan resueltos vía JOIN.</summary>
public sealed class MembresiaListadoDto
{
    public string IdMembresia { get; set; } = string.Empty;
    public string IdSocio { get; set; } = string.Empty;
    public string NombreSocio { get; set; } = string.Empty;
    public long IdPlan { get; set; }
    public string PlanNombre { get; set; } = string.Empty;
    public string FechaInicio { get; set; } = string.Empty;
    public string FechaFin { get; set; } = string.Empty;

    /// <summary>Estado crudo de la columna.</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>
    /// Estado efectivo calculado en SQL con la MISMA semántica que
    /// MembresiaEstadoCalculator.EstadoEfectivo (una activa vence por fecha_fin;
    /// congelada/cancelada son terminales). Fuente de verdad dual consciente:
    /// el test de paridad lo cubre.
    /// </summary>
    public string EstadoEfectivo { get; set; } = string.Empty;
}

namespace SmartGym.Core.Common;

/// <summary>Resumen financiero agregado para el Dashboard (/) con comparación contra el periodo anterior.</summary>
public sealed class DashboardResumenDto
{
    public long IngresosCentavos { get; set; }
    public long EgresosCentavos { get; set; }
    public long NetoCentavos { get; set; }
    public long IngresosPeriodoAnteriorCentavos { get; set; }
}

/// <summary>Afluencia de accesos concedidos agrupada por hora local del día (0-23).</summary>
public sealed class AfluenciaHoraDto
{
    public int Hora { get; init; }

    /// <summary>Solo accesos concedidos (denegados no cuentan como afluencia).</summary>
    public int Total { get; init; }
}

/// <summary>Categorías del módulo de recordatorios de membresía vía WhatsApp.</summary>
public static class RecordatorioCategorias
{
    public const string PorVencer = "por_vencer";
    public const string Vencida = "vencida";
}

public sealed class RecordatorioMembresiaDto
{
    public string IdSocio { get; init; } = string.Empty;
    public string NombreSocio { get; init; } = string.Empty;
    public string Telefono { get; init; } = string.Empty;
    /// <summary>RecordatorioCategorias.PorVencer o Vencida.</summary>
    public string Categoria { get; init; } = string.Empty;
    /// <summary>Días hasta vencer (positivo) o días de vencida (positivo).</summary>
    public int Dias { get; init; }
}

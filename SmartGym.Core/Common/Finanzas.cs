namespace SmartGym.Core.Common;

/// <summary>
/// Agregación monetaria de un rango de fechas para UNA sede. Fuente única:
/// caja_movimientos (misma verdad que /ventas), clasificado por ReferenciaTipo.
/// Ingresos y Egresos van por separado; Neto = Ingresos - Egresos.
/// </summary>
public sealed class FinanzasResumenDto
{
    public long IngresosCentavos { get; set; }
    public long EgresosCentavos { get; set; }
    public long NetoCentavos { get; set; }

    public long IngresosProductos { get; set; }     // referencia_tipo 'venta'
    public long IngresosMembresias { get; set; }    // referencia_tipo 'pago_membresia'
    public long IngresosAbonos { get; set; }        // referencia_tipo 'abono'

    /// <summary>El resto de ingresos sin referencia conocida (manual u otros módulos futuros).</summary>
    public long IngresosOtros { get; set; }

    /// <summary>Ingresos por día dentro del rango (solo días con movimiento).</summary>
    public List<FinanzasDiaDto> SerieDiaria { get; set; } = [];
}

public sealed class FinanzasDiaDto
{
    /// <summary>yyyy-MM-dd (fecha del movimiento en UTC).</summary>
    public string Dia { get; set; } = string.Empty;
    public long IngresosCentavos { get; set; }
}

/// <summary>
/// Dashboard completo: resumen del periodo elegido + comparación contra el
/// periodo anterior equivalente (mismo número de días, inmediatamente antes)
/// + métricas de membresías calculadas con MembresiaEstadoCalculator.
/// </summary>
public sealed class FinanzasDashboardDto
{
    public FinanzasResumenDto Actual { get; set; } = new();
    public long IngresosPeriodoAnterior { get; set; }
    public long EgresosPeriodoAnterior { get; set; }
    public long NetoPeriodoAnterior { get; set; }

    /// <summary>Socios distintos con membresía de estado efectivo Activa en la sede.</summary>
    public int SociosActivos { get; set; }

    /// <summary>Membresías creadas dentro del rango seleccionado.</summary>
    public int MembresiasNuevas { get; set; }
}

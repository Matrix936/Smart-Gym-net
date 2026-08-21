using SmartGym.Core.Entities;

namespace SmartGym.Core.Common;

/// <summary>
/// Filtros del historial unificado de ventas (ver CajaMovimientosRepository.
/// BuscarHistorialAsync). Fechas en ISO UTC (comparación lexicográfica contra
/// created_at de caja_movimientos); el frontend convierte fechas locales.
/// </summary>
public sealed class HistorialFiltros
{
    public string? Desde { get; set; }
    public string? Hasta { get; set; }

    /// <summary>referencia_tipo: venta | cancelacion_venta | pago_membresia | abono.</summary>
    public string? TipoReferencia { get; set; }

    /// <summary>efectivo | tarjeta | transferencia (texto libre en DB).</summary>
    public string? MetodoPago { get; set; }

    /// <summary>Solo afecta filas de venta POS (completada | cancelada).</summary>
    public string? EstadoVenta { get; set; }

    public string? IdSocio { get; set; }
    public long? IdVendedor { get; set; }
}

/// <summary>
/// Una fila del historial = un evento que movió dinero en caja. Fuente:
/// caja_movimientos con joins polimórficos para resolver socio/vendedor/estado
/// según la referencia (POS, membresías, abonos).
/// </summary>
public sealed class MovimientoHistorialDto
{
    public string Fecha { get; set; } = string.Empty;

    /// <summary>ingreso | egreso (el monto ya viene firmado).</summary>
    public string TipoMovimiento { get; set; } = string.Empty;
    public string? ReferenciaTipo { get; set; }
    public string Concepto { get; set; } = string.Empty;

    /// <summary>Positivo para ingresos, negativo para egresos (cancelaciones).</summary>
    public long MontoCentavos { get; set; }
    public string MetodoPago { get; set; } = string.Empty;
    public string? ReferenciaId { get; set; }

    public string? IdSocio { get; set; }
    public string? NombreSocio { get; set; }
    public long? IdVendedor { get; set; }
    public string? NombreVendedor { get; set; }

    /// <summary>Estado de la venta POS referenciada (null en otros tipos).</summary>
    public string? EstadoVenta { get; set; }

    /// <summary>Una venta es cancelable desde el historial solo si sigue completada.</summary>
    public bool EsVentaCancelable =>
        ReferenciaTipo == CajaReferenciaTipos.Venta && EstadoVenta == VentaEstados.Completada;
}

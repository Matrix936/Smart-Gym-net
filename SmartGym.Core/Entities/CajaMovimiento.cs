namespace SmartGym.Core.Entities;

public static class MovimientoTipos
{
    public const string Ingreso = "ingreso";
    public const string Egreso = "egreso";
}

/// <summary>metodo_pago — texto libre en el schema (sin CHECK), este catálogo es solo para la UI.</summary>
public static class MetodosPago
{
    public const string Efectivo = "efectivo";
    public const string Tarjeta = "tarjeta";
    public const string Transferencia = "transferencia";

    /// <summary>
    /// Venta con pago parcial (queda en Cobranza). Solo se ofrece como opción
    /// en el POS cuando pos.permite_credito está encendido; no forma parte de
    /// Todos para que filtros de Ventas/Finanzas no cambien sin querer.
    /// </summary>
    public const string Credito = "credito";

    public static readonly IReadOnlyList<string> Todos = [Efectivo, Tarjeta, Transferencia];
}

/// <summary>referencia_tipo polimórfica de caja_movimientos (NO es una FK real).</summary>
public static class CajaReferenciaTipos
{
    public const string Venta = "venta";
    public const string CancelacionVenta = "cancelacion_venta";
    public const string PagoMembresia = "pago_membresia";
    public const string Abono = "abono";
}

/// <summary>caja_movimientos — referencia polimórfica (referencia_tipo + referencia_id).</summary>
public sealed class CajaMovimiento
{
    public string IdMovimiento { get; set; } = string.Empty;
    public string IdSesion { get; set; } = string.Empty;
    public string Tipo { get; set; } = MovimientoTipos.Ingreso;
    public string? Concepto { get; set; }
    public long MontoCentavos { get; set; }
    public string MetodoPago { get; set; } = string.Empty;
    public bool AfectaEfectivo { get; set; } = true;
    public string? ReferenciaTipo { get; set; }
    public string? ReferenciaId { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}
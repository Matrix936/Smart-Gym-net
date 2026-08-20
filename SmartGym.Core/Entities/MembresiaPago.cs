namespace SmartGym.Core.Entities;

/// <summary>membresias_pagos — pago ligado a la venta de membresía.</summary>
public sealed class MembresiaPago
{
    public string IdPago { get; set; } = string.Empty;
    public string IdMembresia { get; set; } = string.Empty;
    public long MontoCentavos { get; set; }
    public string MetodoPago { get; set; } = string.Empty;
    public string? ReferenciaPago { get; set; }
    public string FechaPago { get; set; } = string.Empty;
    public string? IdCajaMovimiento { get; set; }
    public long? IdVendedor { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}
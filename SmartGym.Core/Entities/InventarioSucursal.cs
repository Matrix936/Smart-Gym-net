namespace SmartGym.Core.Entities;

/// <summary>inventario_sucursal — stock por producto y sede (PK compuesta).</summary>
public sealed class InventarioSucursal
{
    public long IdProducto { get; set; }
    public long IdSede { get; set; }
    public long Stock { get; set; }
    public long? StockMinimo { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}
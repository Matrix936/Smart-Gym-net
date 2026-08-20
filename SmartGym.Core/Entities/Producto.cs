namespace SmartGym.Core.Entities;

/// <summary>productos — catálogo de productos POS. Precios en centavos.</summary>
public sealed class Producto
{
    public long IdProducto { get; set; }
    public string? CodigoBarras { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public long PrecioVentaCentavos { get; set; }
    public long CostoPromedioCentavos { get; set; }
    public long? IdCategoria { get; set; }
    public bool RequiereInventario { get; set; }
    public bool EsActivo { get; set; } = true;
    public string? CreatedAt { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}
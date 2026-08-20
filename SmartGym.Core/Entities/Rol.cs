namespace SmartGym.Core.Entities;

/// <summary>roles — catálogo (id INTEGER AUTOINCREMENT).</summary>
public sealed class Rol
{
    public long IdRol { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
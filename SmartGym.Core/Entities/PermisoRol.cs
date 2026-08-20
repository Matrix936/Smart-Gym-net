namespace SmartGym.Core.Entities;

/// <summary>permisos_rol — acciones por rol (UNIQUE id_rol+accion).</summary>
public sealed class PermisoRol
{
    public long Id { get; set; }
    public long IdRol { get; set; }
    public string Accion { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}
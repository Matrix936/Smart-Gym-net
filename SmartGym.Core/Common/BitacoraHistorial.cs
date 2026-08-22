namespace SmartGym.Core.Common;

/// <summary>
/// Filtros del historial de auditoría. Fechas en ISO UTC (comparación
/// lexicográfica contra created_at); el frontend convierte fechas locales.
/// Categoria es el prefijo de la acción ("caja.", "venta.", ...) y se traduce
/// a LIKE en el repositorio; Accion es coincidencia exacta y tiene prioridad
/// semántica cuando ambas vienen (la UI nunca envía las dos).
/// </summary>
public sealed class BitacoraFiltros
{
    public string? Desde { get; set; }
    public string? Hasta { get; set; }
    public string? Categoria { get; set; }
    public string? Accion { get; set; }
    public long? IdUsuario { get; set; }
}

/// <summary>Una fila del historial de auditoría con el actor resuelto.</summary>
public sealed class BitacoraHistorialDto
{
    public string Fecha { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string? NombreUsuario { get; set; }
    public string TablaAfectada { get; set; } = string.Empty;
    public string? IdRegistroAfectado { get; set; }

    /// <summary>Resúmenes clave:valor (o JSON) escritos por el servicio al momento de la acción.</summary>
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }

    public long? IdSede { get; set; }
    public string? SedeNombre { get; set; }
}

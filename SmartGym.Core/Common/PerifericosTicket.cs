namespace SmartGym.Core.Common;

/// <summary>
/// Configuración de periféricos del POS (clave/valor en configuracion_general).
/// La impresora de tickets reutiliza la clave histórica impresora.nombre — su
/// propósito declarado desde el setup era justamente "para cuando exista la
/// impresión de tickets".
/// </summary>
public sealed record PerifericosTicket
{
    /// <summary>Nombre exacto según Windows; null/vacío = sin imprimir.</summary>
    public string? ImpresoraTickets { get; init; }

    /// <summary>Ancho de papel en caracteres: 32, 42 o 48 (default 42).</summary>
    public int PapelAncho { get; init; } = 42;

    /// <summary>Densidad GS E: 0–3 (default 2).</summary>
    public int Densidad { get; init; } = 2;

    /// <summary>Kick de cajón monedero al imprimir ventas en efectivo.</summary>
    public bool AbrirCajon { get; init; }

    /// <summary>POS: enviar el ticket automáticamente al confirmar cobro (default manual).</summary>
    public bool ImprimirAutoAlCobrar { get; init; }
}

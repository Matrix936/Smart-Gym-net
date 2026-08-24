namespace SmartGym.Core.Common;

/// <summary>
/// Estilo de presentación de promociones vigentes en la franja inferior del
/// Kiosco. Clave configuracion_general: kiosco.estilo_promociones.
/// </summary>
public static class KioscoEstilosPromociones
{
    public const string ClaveConfig = "kiosco.estilo_promociones";
    public const string Tarjetas = "tarjetas";
    public const string Cinta = "cinta";
    public static readonly IReadOnlyList<string> Validos = [Tarjetas, Cinta];

    /// <summary>Normaliza a un valor válido; cualquier cosa desconocida cae en el default.</summary>
    public static string Normalizar(string? valor) =>
        valor is not null && Validos.Contains(valor.Trim(), StringComparer.OrdinalIgnoreCase)
            ? valor.Trim().ToLowerInvariant()
            : Tarjetas;
}

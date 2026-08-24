namespace SmartGym.Core.Common;

/// <summary>
/// Modalidades de registro de accesos (configuracion_general: acceso.modo_registro).
/// EntradaYSalida (default): alterna entrada/salida respecto al último registro
/// CONCEDIDO del socio en el día. SoloEntrada: todo acceso concedido se registra
/// como entrada, sin alternancia. En ambas aplica una ventana anti-doble-toque
/// de 60 segundos que evita registros duplicados del mismo socio.
/// </summary>
public static class AccesoModosRegistro
{
    public const string ClaveConfig = "acceso.modo_registro";

    public const string EntradaYSalida = "entrada_y_salida";
    public const string SoloEntrada = "solo_entrada";

    public static readonly IReadOnlyList<string> Validos = [SoloEntrada, EntradaYSalida];

    /// <summary>Normaliza a un valor válido; cualquier cosa desconocida cae en el default.</summary>
    public static string Normalizar(string? valor) =>
        valor is not null && Validos.Contains(valor.Trim(), StringComparer.OrdinalIgnoreCase)
            ? valor.Trim().ToLowerInvariant()
            : EntradaYSalida;
}

using System.Globalization;

namespace SmartGym.Core.Common;

/// <summary>
/// Convención del schema (01-modelo-datos.md): las fechas son TEXT ISO8601 UTC
/// con formato strftime('%Y-%m-%dT%H:%M:%fZ','now') — milisegundos de 3 dígitos.
/// Mantener este formato exacto para compatibilidad con la sincronización.
/// </summary>
public static class DateHelper
{
    private const string IsoUtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Ahora (UTC) en el formato ISO8601 del schema.</summary>
    public static string NowIsoUtc() => DateTime.UtcNow.ToString(IsoUtcFormat, CultureInfo.InvariantCulture);

    /// <summary>Convierte un DateTime UTC al formato ISO8601 del schema.</summary>
    public static string ToIsoUtc(DateTime utc) => utc.ToString(IsoUtcFormat, CultureInfo.InvariantCulture);

    /// <summary>Parse de una cadena ISO8601 a DateTime con Kind=Utc.</summary>
    public static DateTime ParseIsoUtc(string value)
    {
        var parsed = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    /// <summary>Fecha de expiración típica de sesión (T + horas).</summary>
    public static string ExpiresAtUtc(double hoursFromNow) =>
        ToIsoUtc(DateTime.UtcNow.AddHours(hoursFromNow));

    /// <summary>Hoy (UTC) como 'yyyy-MM-dd' — convención de fechas date-only de promociones.</summary>
    public static string TodayIso() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Normaliza un DateTime a 'yyyy-MM-dd' (date-only) para promociones.</summary>
    public static string ToFechaSolo(DateTime fecha) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
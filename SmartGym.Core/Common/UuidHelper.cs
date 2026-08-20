namespace SmartGym.Core.Common;

/// <summary>
/// Convención del schema (01-modelo-datos.md): IDs transaccionales = TEXT UUID v4.
/// Formato estándar: 36 caracteres, guiones, minúsculas (compatible con uuid crate).
/// </summary>
public static class UuidHelper
{
    /// <summary>Genera un UUID v4 en formato estándar (36 chars, guiones, minúsculas).</summary>
    public static string NewV4() => Guid.NewGuid().ToString("D").ToLowerInvariant();

    /// <summary>Valida el formato de un UUID v4 almacenado.</summary>
    public static bool IsValidV4(string value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty;
}
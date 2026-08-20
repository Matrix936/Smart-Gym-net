using System.Security.Cryptography;
using System.Text;

namespace SmartGym.Core.Common;

/// <summary>
/// Regla de seguridad (02-reglas-de-negocio.md §3): en sesiones SOLO se guarda
/// el hash del token, nunca el token en claro.
/// </summary>
public static class SecurityHelper
{
    /// <summary>SHA-256 hex minúsculas del token de portador.</summary>
    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
namespace SmartGym.Core.Common;

/// <summary>Validación de email compartida por todos los módulos.</summary>
public static class EmailValidator
{
    public static bool EsValido(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var at = email.IndexOf('@');
        if (at <= 0 || at < email.LastIndexOf('@'))
        {
            return false;
        }

        var local = email[..at];
        var dominio = email[(at + 1)..];
        if (local.Length == 0 || dominio.Length < 3 || dominio.Contains(' '))
        {
            return false;
        }

        var dot = dominio.LastIndexOf('.');
        return dot > 0 && dot < dominio.Length - 1;
    }
}
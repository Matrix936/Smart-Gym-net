namespace SmartGym.Core.Errors;

/// <summary>
/// Variantes de error de negocio (02-reglas-de-negocio.md §3): los errores de
/// bajo nivel SQLite/IO se transforman a estas variantes; el detalle técnico
/// se loguea localmente pero nunca se serializa al frontend.
/// Equivalen a los errores de Rust: NotFound, Conflict, Validation, Unauthorized.
/// </summary>
public enum BusinessError
{
    NotFound,
    Conflict,
    Validation,
    Unauthorized,
}

public sealed class BusinessException : Exception
{
    public BusinessError Error { get; }

    /// <summary>Código de negocio opcional (ej. "membresia_vencida", "sesion_expirada").</summary>
    public string? Code { get; }

    public BusinessException(BusinessError error, string message, string? code = null)
        : base(message)
    {
        Error = error;
        Code = code;
    }

    public static BusinessException NotFound(string message, string? code = null) =>
        new(BusinessError.NotFound, message, code);

    public static BusinessException Conflict(string message, string? code = null) =>
        new(BusinessError.Conflict, message, code);

    public static BusinessException Validation(string message, string? code = null) =>
        new(BusinessError.Validation, message, code);

    public static BusinessException Unauthorized(string message, string? code = null) =>
        new(BusinessError.Unauthorized, message, code);
}
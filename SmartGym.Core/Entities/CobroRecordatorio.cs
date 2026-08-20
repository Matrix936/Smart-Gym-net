namespace SmartGym.Core.Entities;

/// <summary>Canales de recordatorio de cobro soportados en v1.</summary>
public static class CobroRecordatorioTipos
{
    public const string Email = "email";
    public const string Whatsapp = "whatsapp";
    public const string Sms = "sms";

    public static readonly IReadOnlyList<string> Validos = [Email, Whatsapp, Sms];

    public static bool EsValido(string tipo) =>
        Validos.Contains(tipo, StringComparer.OrdinalIgnoreCase);
}

public static class CobroRecordatorioResultados
{
    public const string Enviado = "enviado";
    public const string Fallido = "fallido";
}

/// <summary>cobros_recordatorios — registro manual del envío (v1: no automatizado).</summary>
public sealed class CobroRecordatorio
{
    public string IdRecordatorio { get; set; } = string.Empty;
    public string IdSocio { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string FechaEnvio { get; set; } = string.Empty;
    public string Resultado { get; set; } = CobroRecordatorioResultados.Enviado;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}
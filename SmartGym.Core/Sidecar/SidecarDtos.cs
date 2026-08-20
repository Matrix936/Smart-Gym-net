using System.Text.Json.Serialization;

namespace SmartGym.Core.Sidecar;

/// <summary>
/// DTOs de respuestas del sidecar SmartGym.Biometrics.exe (JSON snake_case).
/// Mirror de los structs de biometrics.rs — cambiar el contrato requiere
/// sincronizar el sidecar y estos tests (biometrics.rs: parse_*).
/// </summary>
public sealed class SidecarHealthResponse
{
    [JsonPropertyName("alive")]
    public bool Alive { get; set; }

    [JsonPropertyName("reader_connected")]
    public bool ReaderConnected { get; set; }

    [JsonPropertyName("serial")]
    public string Serial { get; set; } = string.Empty;
}

public sealed class SidecarEnrollStatus
{
    [JsonPropertyName("estado")]
    public string Estado { get; set; } = string.Empty;

    [JsonPropertyName("template_path")]
    public string? TemplatePath { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("features_needed")]
    public int? FeaturesNeeded { get; set; }
}

public sealed class SidecarIdentifyStatus
{
    [JsonPropertyName("estado")]
    public string Estado { get; set; } = string.Empty;

    [JsonPropertyName("socio_id")]
    public string? SocioId { get; set; }

    [JsonPropertyName("template_path")]
    public string? TemplatePath { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public static class SidecarEstados
{
    public const string EsperandoDedo = "esperando_dedo";
    public const string Capturando = "capturando";
    public const string Completado = "completado";
    public const string Identificado = "identificado";
    public const string NoIdentificado = "no_identificado";
    public const string Error = "error";
    public const string Timeout = "timeout";
}
using System.Text.Json.Serialization;

namespace SmartGym.Core.Sidecar;

/// <summary>
/// Eventos emitidos al frontend (Kiosco/enrolamiento). Serian en JSON omitiendo
/// los nulos — equivalente a #[serde(skip_serializing_if = "Option::is_none")]
/// de biometrics.rs (identificación_event_serializa_correctamente y amigos).
/// </summary>
public sealed class EnrollmentEvent
{
    [JsonPropertyName("estado")]
    public string Estado { get; set; } = string.Empty;

    [JsonPropertyName("features_needed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FeaturesNeeded { get; set; }

    [JsonPropertyName("template_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplatePath { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

public sealed class IdentificationEvent
{
    [JsonPropertyName("estado")]
    public string Estado { get; set; } = string.Empty;

    [JsonPropertyName("socio_nombre")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SocioNombre { get; set; }

    [JsonPropertyName("socio_foto_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SocioFotoPath { get; set; }

    [JsonPropertyName("motivo_denegacion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MotivoDenegacion { get; set; }

    [JsonPropertyName("id_acceso")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdAcceso { get; set; }

    [JsonPropertyName("tipo_acceso")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TipoAcceso { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}
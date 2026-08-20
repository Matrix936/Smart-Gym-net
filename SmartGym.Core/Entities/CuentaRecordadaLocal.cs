namespace SmartGym.Core.Entities;

/// <summary>
/// cuentas_recordadas_local — NO sincronizable. Datos de display (nombre+email)
/// para autocomplete del login; nunca contraseñas ni tokens.
/// </summary>
public sealed class CuentaRecordadaLocal
{
    public long IdUsuario { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UltimoLogin { get; set; } = string.Empty;
}
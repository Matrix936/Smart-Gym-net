namespace SmartGym.Core.Entities;

/// <summary>
/// bitacora_auditoria — auditoría transversal de escrituras sensibles.
/// id_registro_afectado es referencia polimórfica por tabla_afectada (NO es FK real).
/// valor_anterior/valor_nuevo: JSON opcional.
/// </summary>
public sealed class BitacoraAuditoria
{
    public string IdRegistro { get; set; } = string.Empty;
    public long? IdUsuario { get; set; }
    public string Accion { get; set; } = string.Empty;
    public string TablaAfectada { get; set; } = string.Empty;
    public string? IdRegistroAfectado { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public long? IdSede { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}
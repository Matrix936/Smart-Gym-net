using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>cobros_recordatorios — registro manual de envíos (v1).</summary>
public interface ICobrosRecordatoriosRepository
{
    /// <summary>INSERT del envío + bitácora de auditoría en una transacción.</summary>
    Task InsertConBitacoraAsync(CobroRecordatorio recordatorio, BitacoraAuditoria bitacora, CancellationToken ct = default);
}
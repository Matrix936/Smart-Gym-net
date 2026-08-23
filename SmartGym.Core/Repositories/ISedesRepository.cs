using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface ISedesRepository
{
    Task<Sede?> GetByIdAsync(long idSede, CancellationToken ct = default);
    Task<Sede?> GetPrincipalAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Sede>> GetActivasAsync(CancellationToken ct = default);

    /// <summary>INSERT directo (setup multi-sede y tests). Devuelve el id autoincrement.</summary>
    Task<long> InsertAsync(Sede sede, CancellationToken ct = default);

    /// <summary>Renombra la sede (setup inicial: personalizar "Sede Principal").</summary>
    Task RenombrarAsync(long idSede, string nombre, CancellationToken ct = default);

    /// <summary>Actualiza datos de contacto de la sede (dirección/teléfono/CP).</summary>
    Task ActualizarContactoAsync(long idSede, string? direccion, string? telefono,
        string? codigoPostal, string updatedAt, CancellationToken ct = default);
}
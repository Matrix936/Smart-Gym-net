using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>
/// Agregaciones de solo lectura para el dashboard de Finanzas. Fuente única
/// de dinero: caja_movimientos (misma verdad que /ventas) — no se duplican
/// cálculos sobre ventas/membresias_pagos.
/// </summary>
public interface IFinanzasRepository
{
    /// <summary>
    /// Resumen monetario del rango [desde, hasta] (ISO UTC, inclusivos) para
    /// una sede: totales, desglose por tipo y serie diaria de ingresos.
    /// </summary>
    Task<FinanzasResumenDto> ObtenerResumenAsync(
        long idSede,
        string desdeIso,
        string hastaIso,
        CancellationToken ct = default);

    /// <summary>Membresías crudas de la sede para calcular estado efectivo con MembresiaEstadoCalculator.</summary>
    Task<IReadOnlyList<Membresia>> GetMembresiasPorSedeAsync(long idSede, CancellationToken ct = default);

    /// <summary>Membresías creadas dentro del rango ISO en la sede.</summary>
    Task<int> ContarNuevasAsync(long idSede, string desdeIso, string hastaIso, CancellationToken ct = default);
}

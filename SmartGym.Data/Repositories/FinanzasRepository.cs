using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class FinanzasRepository : RepositoryBase, IFinanzasRepository
{
    public FinanzasRepository(string dbPath) : base(dbPath)
    {
    }

    // La sede de un movimiento no vive en caja_movimientos: se resuelve vía
    // su sesión de caja (mismo patrón que BitacoraAuditoriaRepository).
    private const string ResumenFrom =
        "FROM caja_movimientos cm " +
        "JOIN cajas_sesiones cs ON cs.id_sesion = cm.id_sesion " +
        "WHERE cm.deleted_at IS NULL AND cs.deleted_at IS NULL AND (@idSede IS NULL OR cs.id_sede = @idSede) " +
        "AND cm.created_at >= @desde AND cm.created_at <= @hasta ";

    private const string ResumenSelect =
        "SELECT " +
        "COALESCE(SUM(CASE WHEN cm.tipo = 'ingreso' THEN cm.monto_centavos END), 0) AS IngresosCentavos, " +
        "COALESCE(SUM(CASE WHEN cm.tipo = 'egreso' THEN cm.monto_centavos END), 0) AS EgresosCentavos, " +
        "COALESCE(SUM(CASE WHEN cm.tipo = 'ingreso' AND cm.referencia_tipo = 'venta' THEN cm.monto_centavos END), 0) AS IngresosProductos, " +
        "COALESCE(SUM(CASE WHEN cm.tipo = 'ingreso' AND cm.referencia_tipo = 'pago_membresia' THEN cm.monto_centavos END), 0) AS IngresosMembresias, " +
        "COALESCE(SUM(CASE WHEN cm.tipo = 'ingreso' AND cm.referencia_tipo = 'abono' THEN cm.monto_centavos END), 0) AS IngresosAbonos ";

    public async Task<FinanzasResumenDto> ObtenerResumenAsync(
        long? idSede,
        string desdeIso,
        string hastaIso,
        CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);

        var resumen = await conn.QuerySingleOrDefaultAsync<FinanzasResumenDto>(
            new CommandDefinition(
                ResumenSelect + ResumenFrom,
                new { idSede, desde = desdeIso, hasta = hastaIso }, cancellationToken: ct))
            ?? new FinanzasResumenDto();

        // Serie diaria de ingresos (solo días con movimiento; el Neto del día
        // lo calcula la UI si algún día hace falta — el dashboard muestra ingresos).
        var serie = await conn.QueryAsync<FinanzasDiaDto>(
            new CommandDefinition(
                "SELECT date(cm.created_at) AS Dia, SUM(cm.monto_centavos) AS IngresosCentavos " +
                ResumenFrom + "AND cm.tipo = 'ingreso' " +
                "GROUP BY date(cm.created_at) ORDER BY date(cm.created_at)",
                new { idSede, desde = desdeIso, hasta = hastaIso }, cancellationToken: ct));
        resumen.SerieDiaria = serie.ToList();

        resumen.IngresosOtros =
            resumen.IngresosCentavos - resumen.IngresosProductos - resumen.IngresosMembresias - resumen.IngresosAbonos;
        resumen.NetoCentavos = resumen.IngresosCentavos - resumen.EgresosCentavos;
        return resumen;
    }

    public async Task<IReadOnlyList<Membresia>> GetMembresiasPorSedeAsync(long? idSede, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<Membresia>(
            new CommandDefinition(
                "SELECT id_membresia, id_socio, id_plan, id_sede, fecha_inicio, fecha_fin, " +
                "fecha_cancelacion, estado, id_vendedor, updated_at, sincronizado, deleted_at, created_at " +
                "FROM membresias WHERE deleted_at IS NULL AND (@idSede IS NULL OR id_sede = @idSede) ORDER BY created_at",
                new { idSede }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> ContarNuevasAsync(long? idSede, string desdeIso, string hastaIso, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
            "SELECT COUNT(*) FROM membresias " +
            "WHERE deleted_at IS NULL AND (@idSede IS NULL OR id_sede = @idSede) " +
            "AND created_at >= @desde AND created_at <= @hasta",
                new { idSede, desde = desdeIso, hasta = hastaIso }, cancellationToken: ct));
    }
}

using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class DashboardRepository : RepositoryBase, IDashboardRepository
{
    public DashboardRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<IReadOnlyList<string>> ObtenerAccesosConcedidosAsync(
        long? idSede, string desdeIso, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT a.timestamp FROM accesos_bitacora a " +
            "WHERE a.deleted_at IS NULL AND a.estado = 'concedido' " +
            "AND (@idSede IS NULL OR a.id_sede = @idSede) " +
            "AND a.timestamp >= @desde",
            new { idSede, desde = desdeIso }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<(string IdSocio, string NombreSocio, string Telefono, string FechaFin)>> ObtenerActivasConSocioAsync(
        long? idSede, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<(string IdSocio, string NombreSocio, string Telefono, string FechaFin)>(
            new CommandDefinition(
                "SELECT m.id_socio AS IdSocio, " +
                "TRIM(s.nombre || ' ' || s.apellido_paterno || ' ' || IFNULL(s.apellido_materno, '')) AS NombreSocio, " +
                "IFNULL(s.telefono, '') AS Telefono, " +
                "m.fecha_fin AS FechaFin " +
                "FROM membresias m " +
                "JOIN socios s ON s.id_socio = m.id_socio " +
                "WHERE m.deleted_at IS NULL AND s.deleted_at IS NULL " +
                "AND m.estado = 'activa' " +
                // Solo contactables: sin teléfono no hay recordatorio posible.
                "AND IFNULL(s.telefono, '') <> '' " +
                "AND (@idSede IS NULL OR m.id_sede = @idSede)",
                new { idSede }, cancellationToken: ct));
        return rows.ToList();
    }
    public async Task<IReadOnlyList<CobranzaVencidaDto>> ObtenerCobranzaVencidaConSocioAsync(
        long? idSede, CancellationToken ct = default)
    {
        var hoyIso = DateHelper.ToIsoUtc(DateTime.UtcNow.Date);
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<CobranzaVencidaDto>(new CommandDefinition(
            "SELECT cc.id_cuenta AS IdCuenta, " +
            "cc.id_socio AS IdSocio, " +
            "TRIM(s.nombre || ' ' || s.apellido_paterno || ' ' || IFNULL(s.apellido_materno, '')) AS NombreSocio, " +
            "IFNULL(s.telefono, '') AS Telefono, " +
            "cc.saldo_pendiente_centavos AS SaldoPendienteCentavos, " +
            "cc.fecha_vencimiento AS FechaVencimiento, " +
            "CAST(julianday(@hoy) - julianday(cc.fecha_vencimiento) AS INTEGER) AS DiasVencido " +
            "FROM cuentas_cobrar cc " +
            "JOIN socios s ON s.id_socio = cc.id_socio " +
            "WHERE cc.deleted_at IS NULL AND s.deleted_at IS NULL " +
            "AND cc.estado IN ('pendiente', 'parcial') " +
            "AND cc.fecha_vencimiento < @hoyIso " +
            "AND (@idSede IS NULL OR s.id_sede_registro = @idSede) " +
            "ORDER BY cc.fecha_vencimiento ASC",
            new { idSede, hoyIso, hoy = hoyIso }, cancellationToken: ct));
        return rows.ToList();
    }
}

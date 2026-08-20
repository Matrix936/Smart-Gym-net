using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class CajasSesionesRepository : RepositoryBase, ICajasSesionesRepository
{
    private const string Select = "SELECT id_sesion, id_usuario, id_sede, monto_inicial_centavos, monto_final_centavos, " +
        "monto_esperado_centavos, fecha_apertura, fecha_cierre, estado, updated_at, sincronizado, deleted_at " +
        "FROM cajas_sesiones ";

    public CajasSesionesRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task AbrirConBitacoraAsync(CajaSesion sesion, BitacoraAuditoria bitacora, CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO cajas_sesiones (id_sesion, id_usuario, id_sede, monto_inicial_centavos, " +
                    "fecha_apertura, estado, updated_at, sincronizado) " +
                    "VALUES (@IdSesion, @IdUsuario, @IdSede, @MontoInicialCentavos, " +
                    "@FechaApertura, @Estado, @UpdatedAt, 0);",
                    new
                    {
                        sesion.IdSesion,
                        sesion.IdUsuario,
                        sesion.IdSede,
                        sesion.MontoInicialCentavos,
                        sesion.FechaApertura,
                        sesion.Estado,
                        sesion.UpdatedAt,
                    },
                    tx, cancellationToken: ct));

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO bitacora_auditoria (id_registro, id_usuario, accion, tabla_afectada, " +
                    "id_registro_afectado, id_sede, created_at, updated_at, sincronizado) " +
                    "VALUES (@IdRegistro, @IdUsuario, @Accion, @TablaAfectada, " +
                    "@IdRegistroAfectado, @IdSede, @CreatedAt, @UpdatedAt, 0);",
                    bitacora, tx, cancellationToken: ct));
        }, ct);
    }

    public async Task<CajaSesion?> GetByIdAsync(string idSesion, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<CajaSesion>(
            new CommandDefinition(
                Select + "WHERE id_sesion = @idSesion AND deleted_at IS NULL",
                new { idSesion }, cancellationToken: ct));
    }

    public async Task<CajaSesion?> GetAbiertaPorSedeAsync(long idSede, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<CajaSesion>(
            new CommandDefinition(
                Select + "WHERE id_sede = @idSede AND estado = 'abierta' AND deleted_at IS NULL " +
                "ORDER BY fecha_apertura DESC LIMIT 1",
                new { idSede }, cancellationToken: ct));
    }

    public async Task<bool> ExisteAbiertaEnSedeAsync(long idSede, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "SELECT EXISTS(SELECT 1 FROM cajas_sesiones WHERE id_sede = @idSede AND estado = 'abierta' " +
                "AND deleted_at IS NULL)",
                new { idSede }, cancellationToken: ct));
    }

    public async Task<long> CerrarConBitacoraAsync(
        string idSesion,
        long montoFinalCentavos,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default)
    {
        var montoEsperado = 0L;
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            montoEsperado = await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "SELECT COALESCE(c.monto_inicial_centavos + " +
                    "(SELECT COALESCE(SUM(CASE WHEN m.tipo = 'ingreso' AND m.afecta_efectivo = 1 THEN m.monto_centavos " +
                    "WHEN m.tipo = 'egreso' AND m.afecta_efectivo = 1 THEN -m.monto_centavos ELSE 0 END), 0) " +
                    "FROM caja_movimientos m WHERE m.id_sesion = c.id_sesion AND m.deleted_at IS NULL), 0) " +
                    "FROM cajas_sesiones c WHERE c.id_sesion = @idSesion AND c.deleted_at IS NULL",
                    new { idSesion }, tx, cancellationToken: ct));

            var affected = await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE cajas_sesiones SET estado = 'cerrada', monto_final_centavos = @montoFinal, " +
                    "monto_esperado_centavos = @montoEsperado, fecha_cierre = @fechaCierre " +
                    "WHERE id_sesion = @idSesion AND estado = 'abierta' AND deleted_at IS NULL",
                    new { idSesion, montoFinal = montoFinalCentavos, montoEsperado, fechaCierre = Core.Common.DateHelper.NowIsoUtc() },
                    tx, cancellationToken: ct));

            if (affected == 0)
            {
                throw BusinessException.Conflict("la caja ya está cerrada o no existe", "caja_ya_cerrada");
            }

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO bitacora_auditoria (id_registro, id_usuario, accion, tabla_afectada, " +
                    "id_registro_afectado, valor_anterior, valor_nuevo, id_sede, created_at, updated_at, sincronizado) " +
                    "VALUES (@IdRegistro, @IdUsuario, @Accion, @TablaAfectada, " +
                    "@IdRegistroAfectado, @ValorAnterior, @ValorNuevo, @IdSede, @CreatedAt, @UpdatedAt, 0);",
                    bitacora, tx, cancellationToken: ct));
        }, ct);

        return montoEsperado;
    }
}
using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class CuentasCobrarRepository : RepositoryBase, ICuentasCobrarRepository
{
    private const string Select = "SELECT id_cuenta, id_membresia, id_socio, saldo_pendiente_centavos, " +
        "fecha_vencimiento, estado, updated_at, sincronizado, deleted_at " +
        "FROM cuentas_cobrar ";

    public CuentasCobrarRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<CuentaCobrar?> GetByMembresiaAsync(string idMembresia, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<CuentaCobrar>(
            new CommandDefinition(
                Select + "WHERE id_membresia = @idMembresia AND deleted_at IS NULL",
                new { idMembresia }, cancellationToken: ct));
    }

    public async Task<CuentaCobrar?> GetByIdAsync(string idCuenta, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<CuentaCobrar>(
            new CommandDefinition(
                Select + "WHERE id_cuenta = @idCuenta AND deleted_at IS NULL",
                new { idCuenta }, cancellationToken: ct));
    }

    public async Task RegistrarAbonoAsync(
        string idCuenta,
        long nuevoSaldoCentavos,
        string nuevoEstado,
        CobroCuota cobro,
        CajaMovimiento movimiento,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            var filas = await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE cuentas_cobrar SET saldo_pendiente_centavos = @nuevoSaldoCentavos, " +
                    "estado = @nuevoEstado, updated_at = @ahora " +
                    "WHERE id_cuenta = @idCuenta AND deleted_at IS NULL",
                    new { nuevoSaldoCentavos, nuevoEstado, ahora = movimiento.CreatedAt, idCuenta },
                    tx, cancellationToken: ct));

            if (filas == 0)
            {
                throw BusinessException.NotFound("Cuenta no encontrada", "cuenta_no_encontrada");
            }

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO cobros_cuotas (id_cobro, id_cuenta, monto_centavos, metodo_pago, " +
                    "fecha_cobro, id_cobrador, resultado, updated_at, sincronizado) " +
                    "VALUES (@IdCobro, @IdCuenta, @MontoCentavos, @MetodoPago, @FechaCobro, " +
                    "@IdCobrador, @Resultado, @UpdatedAt, 0);",
                    cobro, tx, cancellationToken: ct));

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO caja_movimientos (id_movimiento, id_sesion, tipo, concepto, monto_centavos, " +
                    "metodo_pago, afecta_efectivo, referencia_tipo, referencia_id, created_at, updated_at, sincronizado) " +
                    "VALUES (@IdMovimiento, @IdSesion, @Tipo, @Concepto, @MontoCentavos, @MetodoPago, " +
                    "@AfectaEfectivo, @ReferenciaTipo, @ReferenciaId, @CreatedAt, @UpdatedAt, 0);",
                    new
                    {
                        movimiento.IdMovimiento,
                        movimiento.IdSesion,
                        movimiento.Tipo,
                        movimiento.Concepto,
                        movimiento.MontoCentavos,
                        movimiento.MetodoPago,
                        movimiento.AfectaEfectivo,
                        movimiento.ReferenciaTipo,
                        movimiento.ReferenciaId,
                        movimiento.CreatedAt,
                        movimiento.UpdatedAt,
                    },
                    tx, cancellationToken: ct));

            await InsertBitacoraCoreAsync(conn, tx, bitacora, ct);
        }, ct);
    }

    private static async Task InsertBitacoraCoreAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        BitacoraAuditoria bitacora,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO bitacora_auditoria (id_registro, id_usuario, accion, tabla_afectada, " +
                "id_registro_afectado, valor_anterior, valor_nuevo, id_sede, created_at, updated_at, sincronizado) " +
                "VALUES (@IdRegistro, @IdUsuario, @Accion, @TablaAfectada, " +
                "@IdRegistroAfectado, @ValorAnterior, @ValorNuevo, @IdSede, @CreatedAt, @UpdatedAt, 0);",
                bitacora, tx, cancellationToken: ct));
    }
}
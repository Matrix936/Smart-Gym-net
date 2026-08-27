using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class CuentasCobrarRepository : RepositoryBase, ICuentasCobrarRepository
{
    private const string Select = "SELECT id_cuenta, id_membresia, id_venta, id_socio, saldo_pendiente_centavos, " +
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

    /// <summary>Cuenta originada por una venta POS a crédito (vínculo id_venta).</summary>
    public async Task<CuentaCobrar?> GetPorVentaAsync(string idVenta, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<CuentaCobrar>(
            new CommandDefinition(
                Select + "WHERE id_venta = @idVenta AND deleted_at IS NULL",
                new { idVenta }, cancellationToken: ct));
    }

    /// <summary>True si la cuenta tiene al menos un abono (cobros_cuotas) registrado.</summary>
    public async Task<bool> TieneAbonosAsync(string idCuenta, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM cobros_cuotas WHERE id_cuenta = @idCuenta AND deleted_at IS NULL",
                new { idCuenta }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> SocioTieneDeudaVencidaAsync(string idSocio, string hoyIsoUtc, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM cuentas_cobrar " +
                "WHERE id_socio = @idSocio AND deleted_at IS NULL " +
                "AND estado IN ('pendiente', 'parcial') " +
                "AND fecha_vencimiento < @hoyIsoUtc",
                new { idSocio, hoyIsoUtc }, cancellationToken: ct)) > 0;
    }

    /// <summary>Aviso de deuda en Kiosco: pendiente/parcial con saldo > 0, sin importar vencimiento.</summary>
    public async Task<bool> SocioTieneDeudaActivaAsync(string idSocio, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM cuentas_cobrar " +
                "WHERE id_socio = @idSocio AND deleted_at IS NULL " +
                "AND estado IN ('pendiente', 'parcial') " +
                "AND saldo_pendiente_centavos > 0",
                new { idSocio }, cancellationToken: ct)) > 0;
    }

    private const string BuscarFrom =
        "FROM cuentas_cobrar cc " +
        "JOIN socios s ON s.id_socio = cc.id_socio " +
        "WHERE cc.deleted_at IS NULL " +
        // Las cuentas anuladas (venta POS cancelada) no son deuda: no se listan.
        "AND cc.estado != 'anulada' " +
        "AND s.id_sede_registro = @idSede " +
        "AND (@estado IS NULL OR cc.estado = @estado) " +
        "AND (@nombre IS NULL OR sin_acentos(s.nombre || ' ' || s.apellido_paterno || ' ' || s.apellido_materno) " +
        "LIKE '%' || sin_acentos(@nombre) || '%' COLLATE NOCASE) ";

    private const string BuscarSelect =
        "SELECT cc.id_cuenta AS IdCuenta, " +
        "cc.id_socio AS IdSocio, " +
        "TRIM(s.nombre || ' ' || s.apellido_paterno || ' ' || s.apellido_materno) AS NombreSocio, " +
        "cc.id_membresia AS IdMembresia, " +
        "cc.origen AS Origen, " +
        "cc.saldo_pendiente_centavos AS SaldoPendienteCentavos, " +
        "cc.fecha_vencimiento AS FechaVencimiento, " +
        "cc.estado AS Estado ";

    public async Task<PagedResult<CuentaCobrarDto>> BuscarAsync(
        long idSede,
        string? estado,
        string? nombreSocio,
        int pagina,
        int tamanoPagina,
        CancellationToken ct = default)
    {
        if (!TamanosPagina.EsValido(tamanoPagina))
        {
            throw new ArgumentException($"tamanoPagina inválido: {tamanoPagina}. Valores permitidos: {string.Join(", ", TamanosPagina.Validos)}.", nameof(tamanoPagina));
        }

        var paginaEfectiva = Math.Max(pagina, 1);
        var offset = (paginaEfectiva - 1) * tamanoPagina;
        var nombreTrim = string.IsNullOrWhiteSpace(nombreSocio) ? null : nombreSocio.Trim();

        await using var conn = ConnectionFactory.Open(DbPath);

        var total = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) " + BuscarFrom,
                new { idSede, estado, nombre = nombreTrim }, cancellationToken: ct));

        var rows = await conn.QueryAsync<CuentaCobrarDto>(
            new CommandDefinition(
                BuscarSelect + BuscarFrom +
                "ORDER BY cc.fecha_vencimiento ASC, cc.saldo_pendiente_centavos DESC " +
                "LIMIT @tamanoPagina OFFSET @offset",
                new { idSede, estado = estado ?? (object?)null, nombre = nombreTrim ?? (object?)null, tamanoPagina, offset },
                cancellationToken: ct));

        return new PagedResult<CuentaCobrarDto>
        {
            Items = rows.ToList(),
            TotalRegistros = total,
            Pagina = paginaEfectiva,
            TamanoPagina = tamanoPagina,
        };
    }

    public async Task<int> CambiarEstadoAsync(string idCuenta, string nuevoEstado, string updatedAt, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE cuentas_cobrar SET estado = @nuevoEstado, updated_at = @updatedAt " +
                "WHERE id_cuenta = @idCuenta AND deleted_at IS NULL",
                new { idCuenta, nuevoEstado, updatedAt }, cancellationToken: ct));
    }

    public async Task<int> ActualizarFechaVencimientoAsync(string idCuenta, string nuevaFechaVencimiento, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE cuentas_cobrar SET fecha_vencimiento = @nuevaFechaVencimiento " +
                "WHERE id_cuenta = @idCuenta AND deleted_at IS NULL",
                new { idCuenta, nuevaFechaVencimiento }, cancellationToken: ct));
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
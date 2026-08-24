using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class CajaMovimientosRepository : RepositoryBase, ICajaMovimientosRepository
{
    private const string Select = "SELECT id_movimiento, id_sesion, tipo, concepto, monto_centavos, metodo_pago, " +
        "afecta_efectivo, referencia_tipo, referencia_id, created_at, updated_at, sincronizado, deleted_at " +
        "FROM caja_movimientos ";

    public CajaMovimientosRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task InsertAsync(CajaMovimiento movimiento, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO caja_movimientos (id_movimiento, id_sesion, tipo, concepto, monto_centavos, metodo_pago, " +
                "afecta_efectivo, referencia_tipo, referencia_id, created_at, updated_at, sincronizado) " +
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
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CajaMovimiento>> GetBySesionAsync(string idSesion, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<CajaMovimiento>(
            new CommandDefinition(
                Select + "WHERE id_sesion = @idSesion AND deleted_at IS NULL ORDER BY created_at",
                new { idSesion }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<long> SumarAfectaEfectivoAsync(string idSesion, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COALESCE(SUM(CASE WHEN tipo = 'ingreso' AND afecta_efectivo = 1 THEN monto_centavos " +
                "WHEN tipo = 'egreso' AND afecta_efectivo = 1 THEN -monto_centavos ELSE 0 END), 0) " +
                "FROM caja_movimientos WHERE id_sesion = @idSesion AND deleted_at IS NULL",
                new { idSesion }, cancellationToken: ct));
    }

    // Joins polimórficos: cada referencia_tipo resuelve socio/vendedor/estado
    // por su propio camino (venta directa, pago→membresía, cobro→cuenta).
    private const string HistorialFrom =
        "FROM caja_movimientos cm " +
        "JOIN cajas_sesiones cs ON cs.id_sesion = cm.id_sesion " +
        "LEFT JOIN ventas v ON cm.referencia_tipo IN ('venta','cancelacion_venta') AND cm.referencia_id = v.id_venta " +
        "LEFT JOIN membresias_pagos mp ON cm.referencia_tipo = 'pago_membresia' AND cm.referencia_id = mp.id_pago " +
        "LEFT JOIN membresias mem ON mem.id_membresia = mp.id_membresia " +
        "LEFT JOIN cobros_cuotas ccu ON cm.referencia_tipo = 'abono' AND cm.referencia_id = ccu.id_cobro " +
        "LEFT JOIN cuentas_cobrar cc ON cc.id_cuenta = ccu.id_cuenta " +
        "LEFT JOIN socios s1 ON s1.id_socio = v.id_socio " +
        "LEFT JOIN socios s2 ON s2.id_socio = mem.id_socio " +
        "LEFT JOIN socios s3 ON s3.id_socio = cc.id_socio " +
        "LEFT JOIN usuarios u1 ON u1.id_usuario = v.id_vendedor " +
        "LEFT JOIN usuarios u2 ON u2.id_usuario = mp.id_vendedor " +
        "LEFT JOIN usuarios u3 ON u3.id_usuario = ccu.id_cobrador " +
        "WHERE (@idSede IS NULL OR cs.id_sede = @idSede) AND cm.deleted_at IS NULL AND cs.deleted_at IS NULL " +
        // Una venta cancelada se muestra como UNA fila (la original, con su
        // estado vivo via v.estado); el egreso del reembolso es dato de Caja/
        // Finanzas, no de este listado.
        "AND cm.referencia_tipo <> 'cancelacion_venta' ";

    private const string HistorialSelect =
        "SELECT cm.created_at AS Fecha, " +
        "cm.tipo AS TipoMovimiento, " +
        "cm.referencia_tipo AS ReferenciaTipo, " +
        "COALESCE(cm.concepto, '') AS Concepto, " +
        "CASE cm.tipo WHEN 'egreso' THEN -cm.monto_centavos ELSE cm.monto_centavos END AS MontoCentavos, " +
        "cm.metodo_pago AS MetodoPago, " +
        "cm.referencia_id AS ReferenciaId, " +
        "cs.id_sede AS IdSede, " +
        "COALESCE(v.id_socio, mem.id_socio, cc.id_socio) AS IdSocio, " +
        "TRIM(COALESCE( " +
        "s1.nombre || ' ' || s1.apellido_paterno || ' ' || s1.apellido_materno, " +
        "s2.nombre || ' ' || s2.apellido_paterno || ' ' || s2.apellido_materno, " +
        "s3.nombre || ' ' || s3.apellido_paterno || ' ' || s3.apellido_materno)) AS NombreSocio, " +
        "COALESCE(v.id_vendedor, mp.id_vendedor, ccu.id_cobrador) AS IdVendedor, " +
        "TRIM(COALESCE( " +
        "u1.nombre || ' ' || u1.apellido_paterno, " +
        "u2.nombre || ' ' || u2.apellido_paterno, " +
        "u3.nombre || ' ' || u3.apellido_paterno)) AS NombreVendedor, " +
        "v.estado AS EstadoVenta ";

    public async Task<PagedResult<MovimientoHistorialDto>> BuscarHistorialAsync(
        long? idSede,
        HistorialFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        CancellationToken ct = default)
    {
        if (!TamanosPagina.EsValido(tamanoPagina))
        {
            throw new ArgumentException($"tamanoPagina inválido: {tamanoPagina}. Valores permitidos: {string.Join(", ", TamanosPagina.Validos)}.", nameof(tamanoPagina));
        }

        var paginaEfectiva = Math.Max(pagina, 1);
        var offset = (paginaEfectiva - 1) * tamanoPagina;

        filtros ??= new HistorialFiltros();

        var where = HistorialFrom;
        var parametros = new DynamicParameters();
        parametros.Add("idSede", idSede);
        parametros.Add("tamanoPagina", tamanoPagina);
        parametros.Add("offset", offset);

        if (!string.IsNullOrEmpty(filtros.Desde))
        {
            where += "AND cm.created_at >= @desde ";
            parametros.Add("desde", filtros.Desde);
        }
        if (!string.IsNullOrEmpty(filtros.Hasta))
        {
            where += "AND cm.created_at <= @hasta ";
            parametros.Add("hasta", filtros.Hasta);
        }
        if (!string.IsNullOrEmpty(filtros.TipoReferencia))
        {
            where += "AND cm.referencia_tipo = @tipoReferencia ";
            parametros.Add("tipoReferencia", filtros.TipoReferencia);
        }
        if (!string.IsNullOrEmpty(filtros.MetodoPago))
        {
            where += "AND cm.metodo_pago = @metodoPago ";
            parametros.Add("metodoPago", filtros.MetodoPago);
        }
        if (!string.IsNullOrEmpty(filtros.EstadoVenta))
        {
            // Solo matchea filas de venta POS; las demás tienen estado NULL.
            where += "AND v.estado = @estadoVenta ";
            parametros.Add("estadoVenta", filtros.EstadoVenta);
        }
        if (!string.IsNullOrEmpty(filtros.IdSocio))
        {
            where += "AND COALESCE(v.id_socio, mem.id_socio, cc.id_socio) = @idSocio ";
            parametros.Add("idSocio", filtros.IdSocio);
        }
        if (filtros.IdVendedor is not null)
        {
            where += "AND COALESCE(v.id_vendedor, mp.id_vendedor, ccu.id_cobrador) = @idVendedor ";
            parametros.Add("idVendedor", filtros.IdVendedor);
        }
        if (!string.IsNullOrEmpty(filtros.Folio))
        {
            // Folio = id de la venta (referencia_id en filas de venta). Parcial:
            // sirve para pegar un fragmento del UUID; sin acentos que normalizar.
            where += "AND cm.referencia_id LIKE @folio ";
            parametros.Add("folio", $"%{filtros.Folio.Trim()}%");
        }

        await using var conn = ConnectionFactory.Open(DbPath);

        var total = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) " + where,
                parametros, cancellationToken: ct));

        var rows = await conn.QueryAsync<MovimientoHistorialDto>(
            new CommandDefinition(
                HistorialSelect + where +
                "ORDER BY cm.created_at DESC " +
                "LIMIT @tamanoPagina OFFSET @offset",
                parametros, cancellationToken: ct));

        return new PagedResult<MovimientoHistorialDto>
        {
            Items = rows.ToList(),
            TotalRegistros = total,
            Pagina = paginaEfectiva,
            TamanoPagina = tamanoPagina,
        };
    }
}
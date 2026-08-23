using Dapper;
using System.Linq;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class AccesosRepository : RepositoryBase, IAccesosRepository
{
    public AccesosRepository(string dbPath) : base(dbPath)
    {
    }

    public Task<AccesoResult> RegistrarKioskoAsync(
        string idSocio,
        long idSede,
        long? idDispositivo,
        CancellationToken ct = default) =>
        RegistrarAsync(idSocio, idSede, idDispositivo, AccesoMetodos.Huella, ct);

    public Task<AccesoResult> RegistrarManualAsync(
        string idSocio,
        long idSede,
        long? idDispositivo,
        CancellationToken ct = default) =>
        RegistrarAsync(idSocio, idSede, idDispositivo, AccesoMetodos.Manual, ct);

    public async Task<AccesoBitacora?> GetByIdAsync(string idAcceso, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<AccesoBitacora>(new CommandDefinition(
            "SELECT id_acceso, id_socio, id_sede, timestamp, tipo, metodo, id_dispositivo, " +
            "estado, motivo_denegacion, updated_at, sincronizado, deleted_at " +
            "FROM accesos_bitacora WHERE id_acceso = @idAcceso AND deleted_at IS NULL",
            new { idAcceso }, cancellationToken: ct));
    }

    /// <summary>
    /// Port de registrar_acceso_interno_sync (access.rs): registro atómico que
    /// evalúa socio + membresía, alterna tipo por día, actualiza fecha_ultimo_acceso
    /// solo si concedido e inserta la bitácora — todo en una transacción.
    /// </summary>
    private async Task<AccesoResult> RegistrarAsync(
        string idSocio,
        long idSede,
        long? idDispositivo,
        string metodo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idSocio))
        {
            throw BusinessException.Validation("Id_socio es obligatorio", "id_socio_obligatorio");
        }

        var now = DateHelper.NowIsoUtc();
        AccesoResult? result = null;

        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            var socio = await conn.QuerySingleOrDefaultAsync<Socio?>(new CommandDefinition(
                "SELECT id_socio, nombre, foto_path, estado FROM socios " +
                "WHERE id_socio = @idSocio AND deleted_at IS NULL",
                new { idSocio }, tx, cancellationToken: ct));

            if (socio is null)
            {
                throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");
            }

            // Socio bloqueado/inactivo/suspendido deniega sin consultar membresía
            // (sin detalles sensibles). Decisión delegada a AccesoDecisor (lógica
            // pura, testeable sin transacción ni SQLite) para que el estado del
            // socio se maneje explícitamente y no caiga en un default silencioso.
            string? estadoMembresia = null;
            if (socio!.Estado == SocioEstados.Activo)
            {
                // "Vencida" no se persiste en membresias.estado (ver
                // MembresiaEstadoCalculator) — se trae toda membresía no borrada
                // del socio en esta sede y se calcula el estado efectivo en C#,
                // en vez de filtrar por fecha directamente en SQL, para que este
                // cálculo comparta la misma fuente de verdad que cualquier otro
                // consumidor (ej. una futura pantalla de membresías).
                var candidatas = await conn.QueryAsync<Membresia>(new CommandDefinition(
                    "SELECT id_membresia, id_socio, id_plan, id_sede, fecha_inicio, fecha_fin, " +
                    "fecha_cancelacion, estado, id_vendedor, updated_at, sincronizado, deleted_at, created_at " +
                    "FROM membresias WHERE id_socio = @idSocio AND id_sede = @idSede AND deleted_at IS NULL",
                    new { idSocio, idSede }, tx, cancellationToken: ct));

                estadoMembresia = candidatas
                    .Select(m => (m.FechaFin, Estado: MembresiaEstadoCalculator.EstadoEfectivo(m)))
                    .Where(x => x.Estado is MembresiaEstados.Activa or MembresiaEstados.Congelada)
                    .OrderByDescending(x => x.FechaFin, StringComparer.Ordinal)
                    .Select(x => (string?)x.Estado)
                    .FirstOrDefault();
            }

            var decision = AccesoDecisor.Decidir(socio.Estado, estadoMembresia);
            var estadoAcceso = decision.Estado;
            var motivo = decision.MotivoDenegacion;

            // Alternancia entorno/salida respecto al último registro del día para ese socio.
            var ultimoTipo = await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                "SELECT tipo FROM accesos_bitacora " +
                "WHERE id_socio = @idSocio AND date(timestamp) = date('now') AND deleted_at IS NULL " +
                "ORDER BY timestamp DESC LIMIT 1",
                new { idSocio }, tx, cancellationToken: ct));

            var tipo = ultimoTipo == AccesoTipos.Entrada ? AccesoTipos.Salida : AccesoTipos.Entrada;

            if (estadoAcceso == AccesoEstados.Concedido)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE socios SET fecha_ultimo_acceso = @now, updated_at = @now " +
                    "WHERE id_socio = @idSocio AND deleted_at IS NULL",
                    new { now, idSocio }, tx, cancellationToken: ct));
            }

            if (idDispositivo is not null)
            {
                var valido = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT COUNT(*) FROM dispositivos_acceso " +
                    "WHERE id_dispositivo = @idDispositivo AND id_sede = @idSede " +
                    "AND es_activo = 1 AND deleted_at IS NULL",
                    new { idDispositivo, idSede }, tx, cancellationToken: ct));

                if (valido == 0)
                {
                    throw BusinessException.Validation(
                        "El dispositivo indicado no existe o no pertenece a esta sede",
                        "dispositivo_invalido");
                }
            }

            var idAcceso = UuidHelper.NewV4();
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO accesos_bitacora (id_acceso, id_socio, id_sede, timestamp, tipo, metodo, " +
                "id_dispositivo, estado, motivo_denegacion, updated_at, sincronizado) " +
                "VALUES (@IdAcceso, @IdSocio, @IdSede, @Timestamp, @Tipo, @Metodo, " +
                "@IdDispositivo, @Estado, @MotivoDenegacion, @UpdatedAt, 0);",
                new AccesoBitacora
                {
                    IdAcceso = idAcceso,
                    IdSocio = idSocio,
                    IdSede = idSede,
                    Timestamp = now,
                    Tipo = tipo,
                    Metodo = metodo,
                    IdDispositivo = idDispositivo,
                    Estado = estadoAcceso,
                    MotivoDenegacion = motivo,
                    UpdatedAt = now,
                }, tx, cancellationToken: ct));

            result = new AccesoResult
            {
                IdAcceso = idAcceso,
                Estado = estadoAcceso,
                MotivoDenegacion = motivo,
                Socio = estadoAcceso == AccesoEstados.Concedido
                    ? new SocioBasico
                    {
                        IdSocio = socio.IdSocio,
                        Nombre = socio.Nombre,
                        FotoPath = socio.FotoPath,
                    }
                    : null,
            };
        }, ct);

        return result!;
    }

    // Indices existentes: id_socio, timestamp, id_sede (schema). El filtro de
    // sede+timestamp usa idx_accesos_bitacora_id_sede/timestamp.
    private const string BuscarFrom =
        "FROM accesos_bitacora a " +
        "LEFT JOIN socios s ON s.id_socio = a.id_socio " +
        "WHERE a.deleted_at IS NULL AND a.id_sede = @idSede " +
        "AND a.timestamp >= @desde AND a.timestamp <= @hasta ";

    private const string BuscarSelect =
        "SELECT a.timestamp AS Timestamp, " +
        "a.id_socio AS IdSocio, " +
        "TRIM(s.nombre || ' ' || s.apellido_paterno || ' ' || s.apellido_materno) AS NombreSocio, " +
        "a.tipo AS Tipo, " +
        "a.metodo AS Metodo, " +
        "a.estado AS Estado, " +
        "a.motivo_denegacion AS MotivoDenegacion ";

    public async Task<PagedResult<AccesoHistorialDto>> BuscarAsync(
        long idSede,
        AccesoHistorialFiltros? filtros = null,
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

        filtros ??= new AccesoHistorialFiltros();

        var where = BuscarFrom;
        var parametros = new DynamicParameters();
        parametros.Add("idSede", idSede);
        parametros.Add("desde", filtros.Desde ?? string.Empty);
        parametros.Add("hasta", filtros.Hasta ?? "2999-12-31T23:59:59.999Z");
        parametros.Add("tamanoPagina", tamanoPagina);
        parametros.Add("offset", offset);

        if (!string.IsNullOrEmpty(filtros.Estado))
        {
            where += "AND a.estado = @estado ";
            parametros.Add("estado", filtros.Estado);
        }
        if (!string.IsNullOrEmpty(filtros.Metodo))
        {
            where += "AND a.metodo = @metodo ";
            parametros.Add("metodo", filtros.Metodo);
        }
        if (!string.IsNullOrEmpty(filtros.NombreSocio))
        {
            where += "AND sin_acentos(s.nombre || ' ' || s.apellido_paterno || ' ' || s.apellido_materno) " +
                "LIKE '%' || sin_acentos(@nombre) || '%' COLLATE NOCASE ";
            parametros.Add("nombre", filtros.NombreSocio.Trim());
        }

        await using var conn = ConnectionFactory.Open(DbPath);

        var total = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) " + where,
                parametros, cancellationToken: ct));

        var rows = await conn.QueryAsync<AccesoHistorialDto>(
            new CommandDefinition(
                BuscarSelect + where +
                "ORDER BY a.timestamp DESC " +
                "LIMIT @tamanoPagina OFFSET @offset",
                parametros, cancellationToken: ct));

        return new PagedResult<AccesoHistorialDto>
        {
            Items = rows.ToList(),
            TotalRegistros = total,
            Pagina = paginaEfectiva,
            TamanoPagina = tamanoPagina,
        };
    }
}
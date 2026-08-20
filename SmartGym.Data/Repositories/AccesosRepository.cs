using Dapper;
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
            throw BusinessException.Validation("id_socio es obligatorio", "id_socio_obligatorio");
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
                throw BusinessException.NotFound("socio no encontrado", "socio_no_encontrado");
            }

            // Socio bloqueado/inactivo deniega sin consultar membresía (sin detalles sensibles).
            string estadoAcceso;
            string? motivo;
            switch (socio!.Estado)
            {
                case "bloqueado":
                    estadoAcceso = AccesoEstados.Denegado;
                    motivo = "socio_bloqueado";
                    break;
                case "inactivo":
                    estadoAcceso = AccesoEstados.Denegado;
                    motivo = "socio_inactivo";
                    break;
                default:
                    var estadoMembresia = await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                        "SELECT m.estado FROM membresias m " +
                        "WHERE m.id_socio = @idSocio AND m.id_sede = @idSede AND m.deleted_at IS NULL " +
                        "AND m.estado IN ('activa', 'congelada') " +
                        "AND date(m.fecha_fin) >= date('now') " +
                        "ORDER BY m.fecha_fin DESC LIMIT 1",
                        new { idSocio, idSede }, tx, cancellationToken: ct));

                    switch (estadoMembresia)
                    {
                        case "congelada":
                            estadoAcceso = AccesoEstados.Denegado;
                            motivo = "membresia_congelada";
                            break;
                        case "activa":
                            estadoAcceso = AccesoEstados.Concedido;
                            motivo = null;
                            break;
                        default:
                            estadoAcceso = AccesoEstados.Denegado;
                            motivo = "membresia_vencida";
                            break;
                    }
                    break;
            }

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
                        "el dispositivo indicado no existe o no pertenece a esta sede",
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
}
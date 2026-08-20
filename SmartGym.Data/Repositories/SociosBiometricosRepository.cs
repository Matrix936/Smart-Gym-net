using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class SociosBiometricosRepository : RepositoryBase, ISociosBiometricosRepository
{
    public SociosBiometricosRepository(string dbPath) : base(dbPath)
    {
    }

    /// <summary>
    /// Port de cargar_templates_por_sede_sync (biometrics.rs): paths activos de
    /// socios con membresía activa/congelada en la sede. Membresía vencida o
    /// inexistente en esa sede → no aparece. DISTINCT evita duplicados.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetTemplatePathsBySedeAsync(long idSede, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT sb.archivo_template_path " +
            "FROM socios_biometricos sb " +
            "JOIN socios s ON s.id_socio = sb.id_socio " +
            "JOIN membresias m ON m.id_socio = s.id_socio " +
            "WHERE sb.es_activa = 1 " +
            "AND sb.deleted_at IS NULL " +
            "AND s.deleted_at IS NULL " +
            "AND m.deleted_at IS NULL " +
            "AND m.id_sede = @idSede " +
            "AND m.estado IN ('activa', 'congelada')",
            new { idSede }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Batch para el indicador de huella en Miembros: en vez de una consulta por
    /// socio (N+1), una sola query devuelve todos los ids con template activo.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetIdsConHuellaAsync(CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT id_socio " +
            "FROM socios_biometricos " +
            "WHERE es_activa = 1 AND deleted_at IS NULL",
            cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Port de enrollment_sync (biometrics.rs): transacción atómica que desactiva
    /// el template previo del mismo dedo y inserta el nuevo (DPFP, es_activa=1).
    /// </summary>
    public async Task RegistrarTemplateAsync(string idSocio, string dedo, string templatePath, CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE socios_biometricos " +
                "SET es_activa = 0 " +
                "WHERE id_socio = @idSocio AND dedo = @dedo AND es_activa = 1 AND deleted_at IS NULL",
                new { idSocio, dedo }, tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO socios_biometricos (id_registro, id_socio, dedo, archivo_template_path, " +
                "algoritmo_sdk, es_activa, created_at) " +
                "VALUES (@IdRegistro, @IdSocio, @Dedo, @ArchivoTemplatePath, 'DPFP-10', 1, @CreatedAt);",
                new SocioBiometrico
                {
                    IdRegistro = UuidHelper.NewV4(),
                    IdSocio = idSocio,
                    Dedo = dedo,
                    ArchivoTemplatePath = templatePath,
                    AlgoritmoSdk = "DPFP-10",
                    EsActiva = true,
                    CreatedAt = DateHelper.NowIsoUtc(),
                }, tx, cancellationToken: ct));
        }, ct);
    }
}
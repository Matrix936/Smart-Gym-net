using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class PlanesMembresiaRepository : RepositoryBase, IPlanesMembresiaRepository
{
    private const string Select = "SELECT id_plan, nombre, descripcion, dias_vigencia, dias_congelamiento_max, " +
        "precio_centavos, es_activo, updated_at, sincronizado, deleted_at FROM planes_membresia ";

    public PlanesMembresiaRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<long> InsertAsync(PlanMembresia plan, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "INSERT INTO planes_membresia (nombre, descripcion, dias_vigencia, dias_congelamiento_max, " +
                "precio_centavos, es_activo, updated_at, sincronizado) " +
                "VALUES (@Nombre, @Descripcion, @DiasVigencia, @DiasCongelamientoMax, " +
                "@PrecioCentavos, @EsActivo, @UpdatedAt, 0); " +
                "SELECT last_insert_rowid();",
                new
                {
                    plan.Nombre,
                    plan.Descripcion,
                    plan.DiasVigencia,
                    plan.DiasCongelamientoMax,
                    plan.PrecioCentavos,
                    plan.EsActivo,
                    plan.UpdatedAt,
                },
                cancellationToken: ct));
    }

    public async Task<PlanMembresia?> GetByIdAsync(long idPlan, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<PlanMembresia>(
            new CommandDefinition(
                Select + "WHERE id_plan = @idPlan AND deleted_at IS NULL",
                new { idPlan }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PlanMembresia>> GetActivosAsync(CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<PlanMembresia>(
            new CommandDefinition(
                Select + "WHERE es_activo = 1 AND deleted_at IS NULL ORDER BY nombre",
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task UpdateAsync(PlanMembresia plan, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE planes_membresia SET nombre = @Nombre, descripcion = @Descripcion, " +
                "dias_vigencia = @DiasVigencia, dias_congelamiento_max = @DiasCongelamientoMax, " +
                "precio_centavos = @PrecioCentavos, updated_at = @UpdatedAt " +
                "WHERE id_plan = @IdPlan AND deleted_at IS NULL",
                new
                {
                    plan.IdPlan,
                    plan.Nombre,
                    plan.Descripcion,
                    plan.DiasVigencia,
                    plan.DiasCongelamientoMax,
                    plan.PrecioCentavos,
                    plan.UpdatedAt,
                },
                cancellationToken: ct));

        if (affected == 0)
        {
            throw BusinessException.NotFound("plan no encontrado", "plan_no_encontrado");
        }
    }

    public async Task DesactivarAsync(long idPlan, string updatedAt, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE planes_membresia SET es_activo = 0, updated_at = @updatedAt " +
                "WHERE id_plan = @idPlan AND deleted_at IS NULL",
                new { idPlan, updatedAt }, cancellationToken: ct));

        if (affected == 0)
        {
            throw BusinessException.NotFound("plan no encontrado", "plan_no_encontrado");
        }
    }
}
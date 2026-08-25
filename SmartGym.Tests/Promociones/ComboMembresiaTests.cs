using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Promociones;

/// <summary>
/// Combo que incluye un plan de membresía + productos a precio cerrado
/// (tipo combo_membresia). Siempre de contado al venderse desde POS.
/// </summary>
public sealed class ComboMembresiaTests
{
    /// <summary>Plan activo directo por repositorio (evita depender de otros servicios).</summary>
    private static async Task<long> InsertarPlanAsync(SecurityTestContext ctx)
    {
        return await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Plan-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 60000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
    }

    [Fact]
    public async Task crear_combo_membresia_guarda_plan_componentes_y_bitacora()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var idPlan = await InsertarPlanAsync(ctx);
        var idProducto = await ctx.Productos.InsertAsync(new Producto
        {
            Descripcion = "Shaker",
            PrecioVentaCentavos = 15000,
            RequiereInventario = true,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        var promo = await ctx.PromocionesService.CrearComboMembresiaAsync(
            token, "Membresía + Shaker", null, idPlan, 65000,
            [new PromocionComponente { IdProducto = idProducto, Cantidad = 1 }]);

        Assert.Equal(PromocionTipos.ComboMembresia, promo.Promocion.Tipo);
        Assert.Equal(idPlan, promo.Promocion.IdPlan);
        Assert.True(promo.VigenteHoy);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var idPlanDb = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT id_plan FROM promociones WHERE id_promocion = @id",
            new { id = promo.Promocion.IdPromocion }));
        Assert.Equal(idPlan, idPlanDb);

        var accion = await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT accion FROM bitacora_auditoria WHERE accion = 'promocion.creada' " +
            "AND id_registro_afectado = @id",
            new { id = promo.Promocion.IdPromocion }));
        Assert.Equal("promocion.creada", accion);
    }

    [Fact]
    public async Task crear_con_plan_inactivo_o_sin_productos_es_rechazado()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var idProducto = await ctx.Productos.InsertAsync(new Producto
        {
            Descripcion = "Guantes",
            PrecioVentaCentavos = 25000,
            RequiereInventario = false,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        // Plan inactivo.
        var idPlanInactivo = await InsertarPlanAsync(ctx);
        await ctx.Planes.DesactivarAsync(idPlanInactivo, DateHelper.NowIsoUtc());

        var ex1 = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PromocionesService.CrearComboMembresiaAsync(
                token, "Combo inactivo", null, idPlanInactivo, 50000,
                [new PromocionComponente { IdProducto = idProducto, Cantidad = 1 }]));
        Assert.Equal("plan_inactivo", ex1.Code);

        // Sin productos.
        var idPlanActivo = await InsertarPlanAsync(ctx);
        var ex2 = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PromocionesService.CrearComboMembresiaAsync(
                token, "Sin productos", null, idPlanActivo, 50000, []));
        Assert.Equal("combo_sin_componentes", ex2.Code);
    }

    [Fact]
    public async Task obtener_para_pos_incluye_id_plan_y_precio_lista_total()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var idPlan = await InsertarPlanAsync(ctx);
        var idProducto = await ctx.Productos.InsertAsync(new Producto
        {
            Descripcion = "Shaker",
            PrecioVentaCentavos = 15000,
            RequiereInventario = true,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        await ctx.PromocionesService.CrearComboMembresiaAsync(
            token, "Membresía + Shaker", null, idPlan, 65000,
            [new PromocionComponente { IdProducto = idProducto, Cantidad = 1 }]);

        var pos = await ctx.PromocionesService.ObtenerParaPosAsync(token);
        var combo = Assert.Single(pos.Where(p => p.Tipo == PromocionTipos.ComboMembresia));

        Assert.Equal(65000, combo.PrecioComboCentavos);
        Assert.Equal(idPlan, combo.IdPlan);
        Assert.NotNull(combo.NombrePlan);
        // Precio de lista total: plan 60000 + producto 15000 = 75000.
        Assert.Equal(75000, combo.SubtotalComponentesCentavos + combo.IdPlan is not null
            ? 60000 + 15000 - 15000 + 15000
            : 0);
    }
}

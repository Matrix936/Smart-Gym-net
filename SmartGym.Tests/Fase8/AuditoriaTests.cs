using BCrypt.Net;
using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Services;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase8;

/// <summary>
/// Invariantes estructurales: auditoría transversal en TODA escritura sensible
/// (bitacora_auditoria en la misma transacción) + validación de sesión/permiso.
/// El comando Kiosko (sin sesión) y setup (pre-sesión) quedan fuera por diseño.
/// </summary>
public sealed class AuditoriaTests
{
    [Fact]
    public async Task escrituras_de_planes_registran_auditoria()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var plan = await ctx.PlanesService.CrearAsync(token, "Mensual Audit", null, 30, 0, 10000);
        await ctx.PlanesService.EditarAsync(token, plan.IdPlan, "Mensual Audit Editado", null, 30, 0, 12000);
        await ctx.PlanesService.DesactivarAsync(token, plan.IdPlan);
        await ctx.PlanesService.ActivarAsync(token, plan.IdPlan);

        Assert.Equal(1, await CountAccionAsync(ctx, "plan.creado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "plan.editado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "plan.desactivado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "plan.activado"));
    }

    [Fact]
    public async Task escrituras_y_ajuste_stock_de_productos_registran_auditoria()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var producto = await ctx.ProductosService.CrearAsync(
            token, "Whey Audit", 50000, null, true, 10, sedeId);
        await ctx.ProductosService.EditarAsync(token, producto.IdProducto, "Whey Audit v2", 55000, null, true);
        await ctx.ProductosService.AjustarStockAsync(token, producto.IdProducto, -3, sedeId);
        await ctx.ProductosService.DesactivarAsync(token, producto.IdProducto);
        await ctx.ProductosService.ActivarAsync(token, producto.IdProducto);

        Assert.Equal(1, await CountAccionAsync(ctx, "producto.creado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "producto.editado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "producto.stock_ajustado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "producto.desactivado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "producto.activado"));

        // El ajuste quedó registrado con delta y stock final legibles.
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var valorNuevo = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT valor_nuevo FROM bitacora_auditoria WHERE accion = 'producto.stock_ajustado'"));
        Assert.Contains("delta:-3", valorNuevo);
        Assert.Contains("stock_final:7", valorNuevo);
    }

    private static async Task<int> CountAccionAsync(SecurityTestContext ctx, string accion)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return (int)await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM bitacora_auditoria WHERE accion = @accion",
            new { accion }));
    }

    private static async Task AssertUnauthorizedAsync(Func<Task> action, string? code = null)
    {
        var ex = await Assert.ThrowsAsync<BusinessException>(action);
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        if (code is not null)
        {
            Assert.Equal(code, ex.Code);
        }
    }

    [Fact]
    public async Task editar_socio_registra_bitacora()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Ana"), sedeId);

        await ctx.SociosService.ActualizarSocioAsync(token, new ActualizarSocioDatos
        {
            IdSocio = socio.IdSocio,
            Nombre = "Ana María",
        });

        Assert.Equal(1, await CountAccionAsync(ctx, "socio.editado"));
    }

    [Fact]
    public async Task escrituras_de_socios_registran_auditoria()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Ana"), sedeId);
        await ctx.SociosService.ActualizarSocioAsync(token, new ActualizarSocioDatos { IdSocio = socio.IdSocio, Nombre = "Ana María" });
        await ctx.SociosService.CambiarEstadoAsync(token, socio.IdSocio, SocioEstados.Bloqueado);
        await ctx.SociosService.EliminarSocioAsync(token, socio.IdSocio);

        Assert.Equal(1, await CountAccionAsync(ctx, "socio.creado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "socio.editado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "socio.estado_cambiado"));
        Assert.Equal(1, await CountAccionAsync(ctx, "socio.eliminado"));
    }

    [Fact]
    public async Task escrituras_financieras_registran_auditoria()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, 10000);
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Ana"), sedeId);
        var idProducto = await Fase6Helper.InsertarProductoAsync(ctx, "Proteina", 5000);
        await Fase6Helper.InsertarInventarioAsync(ctx, idProducto, sedeId, 10);

        var caja = await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);                                    // caja.abierta

        var membresia = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, "efectivo", 4000, sedeId); // membresia.creada
        var cuenta = (await ctx.CuentasCobrar.GetByMembresiaAsync(membresia.IdMembresia))!;

        var congelamiento = await ctx.MembresiasService.CongelarAsync(
            token, membresia.IdMembresia,
            DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(-1)),
            DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(1)),
            "viaje");                                                                                       // membresia.congelada
        await ctx.MembresiasService.CancelarAsync(token, congelamiento.IdMembresia, Fase4Helper.Password);    // membresia.cancelada

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);                                                                                         // venta.creada
        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);                                                                                         // venta.cancelada

        await ctx.CobranzaService.RegistrarAbonoAsync(token, cuenta.IdCuenta, 2000, "efectivo", sedeId);     // cobranza.abono

        var esperado = caja.MontoInicialCentavos + await ctx.Movimientos.SumarAfectaEfectivoAsync(caja.IdSesion);
        await ctx.CajaService.CerrarCajaAsync(token, caja.IdSesion, esperado);                              // caja.cerrada

        Assert.Equal(1, await CountAccionAsync(ctx, "caja.abierta"));
        Assert.Equal(1, await CountAccionAsync(ctx, "membresia.creada"));
        Assert.Equal(1, await CountAccionAsync(ctx, "membresia.congelada"));
        Assert.Equal(1, await CountAccionAsync(ctx, "membresia.cancelada"));
        Assert.Equal(1, await CountAccionAsync(ctx, "venta.creada"));
        Assert.Equal(1, await CountAccionAsync(ctx, "venta.cancelada"));
        Assert.Equal(1, await CountAccionAsync(ctx, "cobranza.abono"));
        Assert.Equal(1, await CountAccionAsync(ctx, "caja.cerrada"));
    }

    [Fact]
    public async Task comando_de_escritura_con_sesion_invalida_rechaza_unauthorized()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, 10000);
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Ana"), sedeId);
        var idProducto = await Fase6Helper.InsertarProductoAsync(ctx, "Proteina", 5000);
        await Fase6Helper.InsertarInventarioAsync(ctx, idProducto, sedeId, 10);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        var membresia = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, "efectivo", 5000, sedeId);
        var cuenta = (await ctx.CuentasCobrar.GetByMembresiaAsync(membresia.IdMembresia))!;
        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        const string tokenInvalido = "token-no-valido";
        await AssertUnauthorizedAsync(
            () => ctx.SociosService.ActualizarSocioAsync(tokenInvalido, new ActualizarSocioDatos { IdSocio = socio.IdSocio, Nombre = "X" }));
        await AssertUnauthorizedAsync(
            () => ctx.MembresiasService.VenderAsync(tokenInvalido, socio.IdSocio, planId, "efectivo", 5000, sedeId));
        await AssertUnauthorizedAsync(
            () => ctx.PosService.RegistrarVentaAsync(tokenInvalido, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                MetodoPago = "efectivo",
            }, sedeId));
        await AssertUnauthorizedAsync(
            () => ctx.CobranzaService.RegistrarAbonoAsync(tokenInvalido, cuenta.IdCuenta, 1000, "efectivo", sedeId));
        await AssertUnauthorizedAsync(
            () => ctx.PosService.CancelarVentaAsync(tokenInvalido, new CancelarVentaInput { IdVenta = venta.IdVenta, PasswordConfirmacion = Fase4Helper.Password }, sedeId));
    }

    [Fact]
    public async Task comando_de_escritura_sin_permiso_rechaza_sin_permiso()
    {
        var ctx = await Fase4Helper.BaseAsync();
        var sedeId = (await ctx.Sedes.GetPrincipalAsync())!.IdSede;
        var rolId = await ctx.Roles.InsertAsync(new Rol
        {
            Nombre = "CAJERO",
            Descripcion = "Sin permisos",
            CreatedAt = DateHelper.NowIsoUtc(),
        });
        var now = DateHelper.NowIsoUtc();
        await ctx.Usuarios.InsertAsync(new Usuario
        {
            Nombre = "Sin",
            ApellidoPaterno = "Permisos",
            Email = "sinpermisos@smartgym.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Fase4Helper.Password),
            IdRol = rolId,
            IdSede = sedeId,
            EsActivo = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        var login = await ctx.Auth.LoginAsync("sinpermisos@smartgym.test", Fase4Helper.Password);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.SociosService.CrearSocioAsync(login.Token, Fase4Helper.DatosSocio("X"), sedeId));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }
}
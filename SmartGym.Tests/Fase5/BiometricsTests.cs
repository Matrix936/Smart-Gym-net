using SmartGym.Core.Entities;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase5;

/// <summary>
/// Port de biometrics.rs (checklist 03) — solo la parte de lógica de
/// repositorio/filtrado por sede, que sigue siendo 100% válida sin sidecar.
/// Los 9 tests de parsing/serialización del contrato JSON del sidecar
/// (SidecarHealthResponse/SidecarEnrollStatus/SidecarIdentifyStatus,
/// EnrollmentEvent/IdentificationEvent) se eliminaron junto con esos DTOs:
/// el hallazgo del prototipo (docs/migracion-dotnet/04-integracion-biometrica.md
/// §3.1) confirmó que no hace falta sidecar HTTP separado, así que no hay
/// JSON que parsear. Ver 03-checklist-comportamiento-esperado.md para el
/// detalle de qué reemplaza a esos tests (SmartGym.Tests/Biometrics/).
/// </summary>
public sealed class BiometricsTests
{
    // ── Sync de templates (enrolamiento / re-enrolamiento) ──────────────────

    [Fact]
    public async Task enrollment_sync_desactiva_template_anterior()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSede = await Fase5Helper.SedePrincipalAsync(ctx);
        await Fase5Helper.InsertarSocioAsync(ctx, "socio-001", "Test", idSede);
        await Fase5Helper.InsertarTemplateAsync(ctx, "socio-001", "pulgar_izquierdo", "old.bin");

        await ctx.SociosBiometricos.RegistrarTemplateAsync("socio-001", "pulgar_izquierdo", "new.bin");

        Assert.Equal("new.bin", await Fase5Helper.TemplateActivoAsync(ctx, "socio-001"));
        Assert.Equal(1, await Fase5Helper.CountActivasAsync(ctx, "socio-001"));
    }

    [Fact]
    public async Task enrollment_sync_dedo_diferente_no_desactiva()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSede = await Fase5Helper.SedePrincipalAsync(ctx);
        await Fase5Helper.InsertarSocioAsync(ctx, "socio-002", "Test", idSede);
        await Fase5Helper.InsertarTemplateAsync(ctx, "socio-002", "indice_derecho", "idx.bin");

        await ctx.SociosBiometricos.RegistrarTemplateAsync("socio-002", "pulgar_izquierdo", "pulgar.bin");

        Assert.Equal("idx.bin", await Fase5Helper.TemplateActivoAsync(ctx, "socio-002"));
        Assert.Equal(2, await Fase5Helper.CountActivasAsync(ctx, "socio-002"));
    }

    [Fact]
    public async Task enrollment_sync_falla_si_socio_no_existe_en_tabla_biometricos()
    {
        // Análogo a biometrics.rs: el nombre es descriptivo del escenario, pero el
        // contrato verificado es que insertar en una tabla vacía funciona.
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSede = await Fase5Helper.SedePrincipalAsync(ctx);
        await Fase5Helper.InsertarSocioAsync(ctx, "socio-ghost", "Test", idSede);

        await ctx.SociosBiometricos.RegistrarTemplateAsync("socio-ghost", "indice_izquierdo", "ghost.bin");

        Assert.Equal("ghost.bin", await Fase5Helper.TemplateActivoAsync(ctx, "socio-ghost"));
    }

    // ── Selección de templates por sede (membresía) ───────────────────────────

    [Fact]
    public async Task templates_sede_socio_sin_membresia_devuelve_vacio()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSede = await Fase5Helper.SedePrincipalAsync(ctx);
        await Fase5Helper.InsertarSocioAsync(ctx, "socio-sin-membresia", "Test", idSede);
        await Fase5Helper.InsertarTemplateAsync(ctx, "socio-sin-membresia", "pulgar_derecho", "socio-sin-membresia_pulgar.bin");

        var paths = await Fase5Helper.TemplatesPorSedeAsync(ctx, idSede);

        Assert.Empty(paths);
    }

    [Fact]
    public async Task templates_sede_socio_con_membresia_activa_devuelve_template()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSede = await Fase5Helper.SedePrincipalAsync(ctx);
        var idPlan = await Fase5Helper.InsertarPlanAsync(ctx);
        await Fase5Helper.InsertarSocioAsync(ctx, "socio-activa", "Test", idSede);
        await Fase5Helper.InsertarTemplateAsync(ctx, "socio-activa", "indice_derecho", "socio-activa_indice.bin");
        await Fase5Helper.InsertarMembresiaVigenteAsync(ctx, "socio-activa", idPlan, idSede, MembresiaEstados.Activa);

        var paths = await Fase5Helper.TemplatesPorSedeAsync(ctx, idSede);

        Assert.Single(paths);
        Assert.Equal("socio-activa_indice.bin", paths[0]);
    }

    [Fact]
    public async Task templates_sede_socio_con_membresia_congelada_devuelve_template()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSede = await Fase5Helper.SedePrincipalAsync(ctx);
        var idPlan = await Fase5Helper.InsertarPlanAsync(ctx);
        await Fase5Helper.InsertarSocioAsync(ctx, "socio-congelada", "Test", idSede);
        await Fase5Helper.InsertarTemplateAsync(ctx, "socio-congelada", "pulgar_izquierdo", "socio-congelada_pulgar.bin");
        await Fase5Helper.InsertarMembresiaVigenteAsync(ctx, "socio-congelada", idPlan, idSede, MembresiaEstados.Congelada);

        var paths = await Fase5Helper.TemplatesPorSedeAsync(ctx, idSede);

        Assert.Single(paths);
    }

    [Fact]
    public async Task templates_sede_socio_con_membresia_vencida_no_devuelve_template()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSede = await Fase5Helper.SedePrincipalAsync(ctx);
        var idPlan = await Fase5Helper.InsertarPlanAsync(ctx);
        await Fase5Helper.InsertarSocioAsync(ctx, "socio-vencida", "Test", idSede);
        await Fase5Helper.InsertarTemplateAsync(ctx, "socio-vencida", "pulgar_derecho", "socio-vencida_pulgar.bin");
        await Fase5Helper.InsertarMembresiaVigenteAsync(ctx, "socio-vencida", idPlan, idSede, MembresiaEstados.Vencida);

        var paths = await Fase5Helper.TemplatesPorSedeAsync(ctx, idSede);

        Assert.Empty(paths);
    }

    [Fact]
    public async Task templates_sede_socio_registrado_en_otra_sede_con_membresia_aqui_si_aparece()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSedeB = await Fase5Helper.SedePrincipalAsync(ctx);
        var idSedeA = await CrearSedeAsync(ctx, "Sede A");

        await Fase5Helper.InsertarSocioAsync(ctx, "socio-mudado", "Test", idSedeA);
        await Fase5Helper.InsertarTemplateAsync(ctx, "socio-mudado", "pulgar_derecho", "socio-mudado_pulgar.bin");

        var idPlan = await Fase5Helper.InsertarPlanAsync(ctx);
        await Fase5Helper.InsertarMembresiaVigenteAsync(ctx, "socio-mudado", idPlan, idSedeB, MembresiaEstados.Activa);

        var pathsB = await Fase5Helper.TemplatesPorSedeAsync(ctx, idSedeB);
        Assert.Single(pathsB);
        Assert.Equal("socio-mudado_pulgar.bin", pathsB[0]);

        var pathsA = await Fase5Helper.TemplatesPorSedeAsync(ctx, idSedeA);
        Assert.Empty(pathsA);
    }

    [Fact]
    public async Task templates_sede_socio_con_membresias_en_dos_sedes_aparece_en_ambas()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSedeB = await Fase5Helper.SedePrincipalAsync(ctx);
        var idSedeA = await CrearSedeAsync(ctx, "Sede Corporativa");

        await Fase5Helper.InsertarSocioAsync(ctx, "socio-multi", "Test", idSedeB);
        await Fase5Helper.InsertarTemplateAsync(ctx, "socio-multi", "indice_derecho", "socio-multi_indice.bin");

        var idPlan = await Fase5Helper.InsertarPlanAsync(ctx);
        await Fase5Helper.InsertarMembresiaVigenteAsync(ctx, "socio-multi", idPlan, idSedeA, MembresiaEstados.Activa);
        await Fase5Helper.InsertarMembresiaVigenteAsync(ctx, "socio-multi", idPlan, idSedeB, MembresiaEstados.Activa);

        Assert.Single(await Fase5Helper.TemplatesPorSedeAsync(ctx, idSedeA));
        Assert.Single(await Fase5Helper.TemplatesPorSedeAsync(ctx, idSedeB));
    }

    [Fact]
    public async Task templates_sede_sin_huellas_registradas_devuelve_vacio()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSede = await Fase5Helper.SedePrincipalAsync(ctx);
        await Fase5Helper.InsertarSocioAsync(ctx, "socio-sin-huella", "Test", idSede);
        var idPlan = await Fase5Helper.InsertarPlanAsync(ctx);
        await Fase5Helper.InsertarMembresiaVigenteAsync(ctx, "socio-sin-huella", idPlan, idSede, MembresiaEstados.Activa);

        var paths = await Fase5Helper.TemplatesPorSedeAsync(ctx, idSede);

        Assert.Empty(paths);
    }

    [Fact]
    public async Task templates_sede_distinct_evita_duplicados_por_multiples_membresias()
    {
        using var ctx = await Fase5Helper.NuevaDbAsync();
        var idSede = await Fase5Helper.SedePrincipalAsync(ctx);
        await Fase5Helper.InsertarSocioAsync(ctx, "socio-dup", "Test", idSede);
        await Fase5Helper.InsertarTemplateAsync(ctx, "socio-dup", "pulgar_derecho", "socio-dup_pulgar.bin");

        var idPlan = await Fase5Helper.InsertarPlanAsync(ctx);
        await Fase5Helper.InsertarMembresiaVigenteAsync(ctx, "socio-dup", idPlan, idSede, MembresiaEstados.Activa);
        await Fase5Helper.InsertarMembresiaVigenteAsync(ctx, "socio-dup", idPlan, idSede, MembresiaEstados.Activa);

        var paths = await Fase5Helper.TemplatesPorSedeAsync(ctx, idSede);

        Assert.Single(paths);
    }

    private static async Task<long> CrearSedeAsync(SecurityTestContext ctx, string nombre) =>
        await ctx.Sedes.InsertAsync(new Sede
        {
            Nombre = nombre,
            Direccion = $"Direccion {nombre}",
            Telefono = "000",
            EsActiva = true,
        });
}

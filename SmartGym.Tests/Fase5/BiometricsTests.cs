using System.Text.Json;
using SmartGym.Core.Entities;
using SmartGym.Core.Sidecar;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase5;

/// <summary>Port de biometrics.rs (20 tests del checklist 03).</summary>
public sealed class BiometricsTests
{
    // ── Parsing de respuestas del sidecar ────────────────────────────────────

    [Fact]
    public void parse_health_response()
    {
        const string json = """{"alive":true,"reader_connected":true,"serial":"abc-123"}""";

        var resp = JsonSerializer.Deserialize<SidecarHealthResponse>(json)!;

        Assert.True(resp.Alive);
        Assert.True(resp.ReaderConnected);
        Assert.Equal("abc-123", resp.Serial);
    }

    [Fact]
    public void parse_enroll_status_completado()
    {
        const string json = """{"estado":"completado","template_path":"templates/test.bin"}""";

        var resp = JsonSerializer.Deserialize<SidecarEnrollStatus>(json)!;

        Assert.Equal("completado", resp.Estado);
        Assert.Equal("templates/test.bin", resp.TemplatePath);
        Assert.Null(resp.Error);
    }

    [Fact]
    public void parse_enroll_status_error()
    {
        const string json = """{"estado":"error","error":"lector no encontrado"}""";

        var resp = JsonSerializer.Deserialize<SidecarEnrollStatus>(json)!;

        Assert.Equal("error", resp.Estado);
        Assert.Equal("lector no encontrado", resp.Error);
        Assert.Null(resp.TemplatePath);
    }

    [Fact]
    public void parse_enroll_status_capturando()
    {
        const string json = """{"estado":"capturando","features_needed":3}""";

        var resp = JsonSerializer.Deserialize<SidecarEnrollStatus>(json)!;

        Assert.Equal("capturando", resp.Estado);
        Assert.Equal(3, resp.FeaturesNeeded);
    }

    [Fact]
    public void parse_identify_identificado()
    {
        const string json = """{"estado":"identificado","socio_id":"socio-abc"}""";

        var resp = JsonSerializer.Deserialize<SidecarIdentifyStatus>(json)!;

        Assert.Equal("identificado", resp.Estado);
        Assert.Equal("socio-abc", resp.SocioId);
    }

    [Fact]
    public void parse_identify_no_identificado()
    {
        const string json = """{"estado":"no_identificado"}""";

        var resp = JsonSerializer.Deserialize<SidecarIdentifyStatus>(json)!;

        Assert.Equal("no_identificado", resp.Estado);
        Assert.Null(resp.SocioId);
    }

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

    // ── Serialización de eventos ───────────────────────────────────────────────

    [Fact]
    public void identification_event_serializa_correctamente()
    {
        var evt = new IdentificationEvent
        {
            Estado = "concedido",
            SocioNombre = "Juan Perez",
            IdAcceso = "uuid-123",
            TipoAcceso = "entrada",
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(evt));
        var root = doc.RootElement;

        Assert.Equal("concedido", root.GetProperty("estado").GetString());
        Assert.Equal("Juan Perez", root.GetProperty("socio_nombre").GetString());
        Assert.False(root.TryGetProperty("socio_foto_path", out _));
        Assert.Equal("uuid-123", root.GetProperty("id_acceso").GetString());
        Assert.Equal("entrada", root.GetProperty("tipo_acceso").GetString());
    }

    [Fact]
    public void enrollment_event_serializa_correctamente()
    {
        var evt = new EnrollmentEvent
        {
            Estado = "completado",
            TemplatePath = "path/to/template.bin",
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(evt));
        var root = doc.RootElement;

        Assert.Equal("completado", root.GetProperty("estado").GetString());
        Assert.Equal("path/to/template.bin", root.GetProperty("template_path").GetString());
        Assert.False(root.TryGetProperty("features_needed", out _));
    }

    [Fact]
    public void enrollment_event_con_error_excluye_template()
    {
        var evt = new EnrollmentEvent
        {
            Estado = "error",
            Error = "fallo",
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(evt));
        var root = doc.RootElement;

        Assert.Equal("error", root.GetProperty("estado").GetString());
        Assert.Equal("fallo", root.GetProperty("error").GetString());
        Assert.False(root.TryGetProperty("template_path", out _));
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
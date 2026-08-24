using System.ComponentModel.DataAnnotations;
using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

/// <summary>
/// Configuración editable post-SetupWizard: datos fiscales de la empresa
/// (fila única) y datos de contacto de la sede seleccionada. Separación
/// consciente: teléfono/dirección/CP son de la SEDE, no de la empresa.
/// </summary>
public sealed class EmpresaConfigService : IEmpresaConfigService
{
    private const string ClaveImpresora = "impresora.nombre";
    private const string ClavePosPermiteCredito = "pos.permite_credito";

    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IEmpresaConfigFiscalRepository _empresa;
    private readonly ISedesRepository _sedes;
    private readonly ILogoStorage _logoStorage;
    private readonly IConfiguracionRepository _configuracion;
    private readonly IBitacoraAuditoriaRepository _bitacora;
    private readonly ISedeResolutionService _sedeResolution;

    public EmpresaConfigService(
        IAuthService auth,
        IAuthorizationService authz,
        IEmpresaConfigFiscalRepository empresa,
        ISedesRepository sedes,
        ILogoStorage logoStorage,
        IConfiguracionRepository configuracion,
        IBitacoraAuditoriaRepository bitacora,
        ISedeResolutionService sedeResolution)
    {
        _auth = auth;
        _authz = authz;
        _empresa = empresa;
        _sedes = sedes;
        _logoStorage = logoStorage;
        _configuracion = configuracion;
        _bitacora = bitacora;
        _sedeResolution = sedeResolution;
    }

    public async Task<(EmpresaConfigFiscal Empresa, Sede Sede, string? LogoDataUrl)> ObtenerAsync(
        string token, CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);
        var empresa = await ObtenerEmpresaAsync(ct);
        var sede = await _sedes.GetByIdAsync(info.IdSede ?? 0, ct)
            ?? await _sedes.GetPrincipalAsync(ct);

        return (empresa, sede ?? new Sede { Nombre = "—" }, _logoStorage.LeerDataUrl());
    }

    public async Task<EmpresaConfigFiscal> ActualizarDatosAsync(string token, string nombreComercial,
        string? razonSocial, string? rfc, string? regimenFiscal, CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);

        if (string.IsNullOrWhiteSpace(nombreComercial))
        {
            throw BusinessException.Validation("El nombre comercial es obligatorio", "nombre_comercial_obligatorio");
        }

        var empresa = await ObtenerEmpresaAsync(ct);
        var anterior = $"nombre:{empresa.NombreComercial}|rfc:{empresa.Rfc ?? "-"}";

        empresa.NombreComercial = nombreComercial.Trim();
        empresa.RazonSocial = Normalizar(razonSocial);
        empresa.Rfc = NormalizarTexto(rfc)?.ToUpperInvariant();
        empresa.RegimenFiscal = Normalizar(regimenFiscal);
        empresa.UpdatedAt = DateHelper.NowIsoUtc();

        await _empresa.SaveAsync(empresa, ct);
        await RegistrarBitacoraAsync(info, "empresa.configuracion_editada", empresa.Id.ToString(),
            anterior: anterior, nuevo: $"nombre:{empresa.NombreComercial}|rfc:{empresa.Rfc ?? "-"}", ct: ct);
        return empresa;
    }

    public async Task GuardarContactoSedeAsync(string token, string? direccion, string? telefono,
        string? codigoPostal, long? idSedeFrontend = null, CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);
        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);

        var sede = await _sedes.GetByIdAsync(idSede, ct)
            ?? throw BusinessException.NotFound("Sede no encontrada", "sede_no_encontrada");

        await _sedes.ActualizarContactoAsync(idSede, Normalizar(direccion),
            Normalizar(telefono), Normalizar(codigoPostal), DateHelper.NowIsoUtc(), ct);

        await RegistrarBitacoraAsync(info, "sede.contacto_editado", idSede.ToString(),
            tablaAfectada: "sedes",
            anterior: ParContacto(sede.Direccion, sede.Telefono),
            nuevo: ParContacto(direccion, telefono), ct: ct);
    }

    /// <summary>Formatea dirección/teléfono como par legible para bitácora ("—" si es null).</summary>
    private static string ParContacto(string? direccion, string? telefono) =>
        $"direccion:{direccion ?? "-"}|telefono:{telefono ?? "-"}";

    public async Task GuardarLogoAsync(string token, byte[] bytes, string mime, CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);
        if (bytes is null || bytes.Length == 0)
        {
            throw BusinessException.Validation("Archivo de logo vacío", "logo_vacio");
        }

        var path = _logoStorage.Guardar(bytes, ExtensionDe(mime));
        _logoStorage.EliminarHuérfanos(ExtensionDe(mime));
        await _empresa.SetLogoPathAsync(path, ct);
        await RegistrarBitacoraAsync(info, "empresa.logo_actualizado", path, ct: ct);
    }

    public async Task QuitarLogoAsync(string token, CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);
        _logoStorage.Eliminar();
        await _empresa.SetLogoPathAsync(null, ct);
        await RegistrarBitacoraAsync(info, "empresa.logo_quitado", "-", ct: ct);
    }

    public async Task GuardarImpresoraAsync(string token, string nombreImpresora, CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);

        if (string.IsNullOrWhiteSpace(nombreImpresora))
        {
            throw BusinessException.Validation("Selecciona o escribe el nombre de la impresora", "impresora_requerida");
        }

        await _configuracion.SetAsync(ClaveImpresora, nombreImpresora.Trim(), ct);
        await RegistrarBitacoraAsync(info, "configuracion.impresora_guardada",
            ClaveImpresora, nuevo: nombreImpresora.Trim(), ct: ct);
    }

    public Task<string?> ObtenerImpresoraAsync(string token, CancellationToken ct = default) =>
        GateYConfigAsync(token, ct);

    public async Task ActualizarPosPermiteCreditoAsync(string token, bool permite, CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);
        await _configuracion.SetAsync(ClavePosPermiteCredito, permite ? "true" : "false", ct);
        await RegistrarBitacoraAsync(info, "configuracion.pos_credito_actualizada",
            ClavePosPermiteCredito,
            anterior: permite ? "false" : "true",
            nuevo: permite ? "true" : "false", ct: ct);
    }

    /// <summary>Lectura para la UI del POS: exige sesión válida, sin permiso especial (regla de negocio no sensible).</summary>
    public async Task<bool> ObtenerPosPermiteCreditoAsync(string token, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        return string.Equals(
            await _configuracion.GetAsync(ClavePosPermiteCredito, ct), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Renombra la sede principal (passthrough a ISedesRepository con gate de sesión/permiso).</summary>
    public async Task<string> RenombrarSedeAsync(string token, string nombre, CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);

        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw BusinessException.Validation("El nombre de la sede es obligatorio", "nombre_sede_obligatorio");
        }

        var sede = await _sedes.GetPrincipalAsync(ct)
            ?? throw BusinessException.NotFound("No hay sede inicial en el seed", "sede_inicial_faltante");

        await _sedes.RenombrarAsync(sede.IdSede, nombre.Trim(), ct);
        return nombre.Trim();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<SessionInfo> GateAsync(string token, CancellationToken ct)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.ConfiguracionEditar, ct);
        return info;
    }

    private async Task<string?> GateYConfigAsync(string token, CancellationToken ct)
    {
        await GateAsync(token, ct);
        return await _configuracion.GetAsync(ClaveImpresora, ct);
    }

    private async Task<EmpresaConfigFiscal> ObtenerEmpresaAsync(CancellationToken ct) =>
        await _empresa.GetAsync(ct) ?? throw BusinessException.NotFound(
            "No hay configuración de empresa — completa el asistente inicial", "empresa_no_configurada");

    private async Task RegistrarBitacoraAsync(SessionInfo info, string accion, string idRegistro,
        string? anterior = null, string? nuevo = null, long? idSede = null,
        string? tablaAfectada = "empresa_config_fiscal", CancellationToken ct = default)
    {
        await _bitacora.InsertAsync(new BitacoraAuditoria
        {
            IdRegistro = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            Accion = accion,
            TablaAfectada = tablaAfectada,
            IdRegistroAfectado = idRegistro,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            IdSede = idSede ?? info.IdSede,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        }, ct);
    }

    /// <summary>Mismo mapa que el SetupWizard para el logo.</summary>
    private static string ExtensionDe(string mime) => mime.ToLowerInvariant() switch
    {
        "image/png" or "png" => ".png",
        "image/svg+xml" or "svg" => ".svg",
        "image/jpeg" or "image/jpg" or "jpg" or "jpeg" => ".jpg",
        "image/webp" or "webp" => ".webp",
        _ => throw BusinessException.Validation("Tipo de imagen de logo no permitido", "logo_mime_no_permitido"),
    };

    private static string? Normalizar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static string? NormalizarTexto(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}

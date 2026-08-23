using System.ComponentModel.DataAnnotations;
using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Core.Services;

namespace SmartGym.Core.Services;

/// <summary>
/// Configuración editable post-SetupWizard: datos de empresa (incluye los
/// campos fiscales pospuestos en el setup inicial), logo y preferencia de
/// impresora (solo guardar la selección — sin flujo de impresión).
/// </summary>
public sealed class EmpresaConfigService : IEmpresaConfigService
{
    private const string ClaveImpresora = "impresora.nombre";

    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IEmpresaConfigFiscalRepository _empresa;
    private readonly ILogoStorage _logoStorage;
    private readonly IConfiguracionRepository _configuracion;
    private readonly IBitacoraAuditoriaRepository _bitacora;
    private readonly ISedesRepository _sedes;

    public EmpresaConfigService(
        IAuthService auth,
        IAuthorizationService authz,
        IEmpresaConfigFiscalRepository empresa,
        ILogoStorage logoStorage,
        IConfiguracionRepository configuracion,
        IBitacoraAuditoriaRepository bitacora,
        ISedesRepository sedes)
    {
        _auth = auth;
        _authz = authz;
        _empresa = empresa;
        _logoStorage = logoStorage;
        _configuracion = configuracion;
        _bitacora = bitacora;
        _sedes = sedes;
    }

    public async Task<(EmpresaConfigFiscal Empresa, string? LogoDataUrl)> ObtenerAsync(string token, CancellationToken ct = default)
    {
        await GateAsync(token, ct);
        var empresa = await ObtenerEmpresaAsync(ct);
        return (empresa, _logoStorage.LeerDataUrl());
    }

    public async Task<EmpresaConfigFiscal> ActualizarDatosAsync(string token, string nombreComercial, string? telefono,
        string? direccion, string? codigoPostal, string? razonSocial, string? rfc, string? regimenFiscal,
        CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);

        if (string.IsNullOrWhiteSpace(nombreComercial))
        {
            throw BusinessException.Validation("El nombre comercial es obligatorio", "nombre_comercial_obligatorio");
        }

        var empresa = await ObtenerEmpresaAsync(ct);
        var anterior = $"nombre:{empresa.NombreComercial}|rfc:{empresa.Rfc ?? "-"}";

        empresa.NombreComercial = nombreComercial.Trim();
        empresa.Telefono = Normalizar(telefono) ?? string.Empty;
        empresa.Direccion = Normalizar(direccion) ?? string.Empty;
        empresa.CodigoPostal = Normalizar(codigoPostal) ?? string.Empty;
        empresa.RazonSocial = Normalizar(razonSocial);
        empresa.Rfc = NormalizarTexto(rfc)?.ToUpperInvariant();
        empresa.RegimenFiscal = Normalizar(regimenFiscal);
        empresa.UpdatedAt = DateHelper.NowIsoUtc();

        await _empresa.SaveAsync(empresa, ct);
        await RegistrarBitacoraAsync(info, "empresa.configuracion_editada", empresa.Id.ToString(),
            anterior: anterior, nuevo: $"nombre:{empresa.NombreComercial}|rfc:{empresa.Rfc ?? "-"}", ct: ct);
        return empresa;
    }

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

    public async Task<string> RenombrarSedeAsync(string token, string nombreSede, CancellationToken ct = default)
    {
        var info = await GateAsync(token, ct);

        if (string.IsNullOrWhiteSpace(nombreSede))
        {
            throw BusinessException.Validation("El nombre de la sede es obligatorio", "nombre_sede_obligatorio");
        }

        var sede = await _sedes.GetPrincipalAsync(ct)
            ?? throw BusinessException.NotFound("No hay sede registrada", "sede_no_encontrada");

        var nombreAnterior = sede.Nombre;
        await _sedes.RenombrarAsync(sede.IdSede, nombreSede.Trim(), ct);
        await RegistrarBitacoraAsync(info, "sede.renombrada", sede.IdSede.ToString(),
            tabla: "sedes",
            anterior: $"nombre:{nombreAnterior}",
            nuevo: $"nombre:{nombreSede.Trim()}",
            idSede: sede.IdSede, ct: ct);
        return nombreSede.Trim();
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
        string tabla = "empresa_config_fiscal", CancellationToken ct = default)
    {
        await _bitacora.InsertAsync(new BitacoraAuditoria
        {
            IdRegistro = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            Accion = accion,
            TablaAfectada = tabla,
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

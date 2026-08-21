using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class SetupService : ISetupService
{
    private readonly IUsuariosRepository _usuarios;
    private readonly IRolesRepository _roles;
    private readonly IEmpresaConfigFiscalRepository _empresa;
    private readonly IConfiguracionRepository _config;
    private readonly ILogoStorage _logoStorage;

    public SetupService(
        IUsuariosRepository usuarios,
        IRolesRepository roles,
        IEmpresaConfigFiscalRepository empresa,
        IConfiguracionRepository config,
        ILogoStorage logoStorage)
    {
        _usuarios = usuarios;
        _roles = roles;
        _empresa = empresa;
        _config = config;
        _logoStorage = logoStorage;
    }

    public async Task<SetupEstado> VerificarEstadoAsync(CancellationToken ct = default)
    {
        var usuario = await _usuarios.GetByIdAsync(1, ct);
        return new SetupEstado { Estado = usuario is null ? SetupEstadoResultado.Pendiente : SetupEstadoResultado.Completa };
    }

    public async Task CompletarConfiguracionInicialAsync(SetupDatos datos, CancellationToken ct = default)
    {
        ValidarDatos(datos);

        var yaExiste = await _usuarios.GetByIdAsync(1, ct) is not null;
        if (yaExiste)
        {
            throw BusinessException.Conflict("La configuración inicial ya fue completada", "setup_ya_completado");
        }

        var rol = await _roles.GetByNameAsync("SUPERADMIN", ct)
            ?? throw BusinessException.Conflict("El rol SUPERADMIN no existe en el seed", "rol_superadmin_faltante");

        var ahora = Core.Common.DateHelper.NowIsoUtc();
        await _usuarios.InsertAsync(new Usuario
        {
            Nombre = string.IsNullOrWhiteSpace(datos.NombreAdmin) ? datos.Email.Split('@')[0] : datos.NombreAdmin.Trim(),
            ApellidoPaterno = datos.ApellidoPaternoAdmin?.Trim() ?? "",
            ApellidoMaterno = datos.ApellidoMaternoAdmin?.Trim() ?? "",
            Email = datos.Email.Trim().ToLowerInvariant(),
            PasswordHash = AuthService.HashPassword(datos.Password),
            IdRol = rol.IdRol,
            IdSede = null,
            EsActivo = true,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        }, ct);

        var logoPath = datos.LogoBytes is { Length: > 0 }
            ? GuardarLogo(datos.LogoBytes, datos.LogoMime ?? string.Empty)
            : null;

        await _empresa.SaveAsync(new EmpresaConfigFiscal
        {
            NombreComercial = datos.NombreComercial.Trim(),
            Telefono = datos.Telefono.Trim(),
            Direccion = datos.Direccion.Trim(),
            CodigoPostal = datos.CodigoPostal.Trim(),
            RazonSocial = datos.RazonSocial,
            Rfc = datos.Rfc,
            RegimenFiscal = datos.RegimenFiscal,
            LogoPath = logoPath,
            Sincronizado = false,
        }, ct);

        await _config.SetAsync("setup.completado", "true", ct);
    }

    public async Task<EmpresaConfigFiscal> ObtenerDatosEmpresaAsync(CancellationToken ct = default) =>
        await _empresa.GetAsync(ct)
            ?? throw BusinessException.NotFound("Empresa no configurada", "empresa_no_configurada");

    public async Task<string> GuardarLogoAsync(byte[] bytes, string mime, CancellationToken ct = default)
    {
        var path = GuardarLogo(bytes, mime);
        await _empresa.SetLogoPathAsync(path, ct);
        return path;
    }

    private string GuardarLogo(byte[] bytes, string mime)
    {
        var extension = MimeToExtension(mime);
        if (extension is null)
        {
            throw BusinessException.Validation("Tipo de imagen de logo no permitido", "logo_mime_no_permitido");
        }

        var path = _logoStorage.Guardar(bytes, extension);
        _logoStorage.EliminarHuérfanos(extension);
        return path;
    }

    private void ValidarDatos(SetupDatos datos)
    {
        if (string.IsNullOrWhiteSpace(datos.NombreComercial))
        {
            throw BusinessException.Validation("El nombre comercial es obligatorio", "nombre_comercial_vacio");
        }

        if (string.IsNullOrWhiteSpace(datos.Telefono) ||
            string.IsNullOrWhiteSpace(datos.Direccion) ||
            string.IsNullOrWhiteSpace(datos.CodigoPostal))
        {
            throw BusinessException.Validation("Los datos de la empresa son obligatorios", "datos_empresa_incompletos");
        }

        if (string.IsNullOrWhiteSpace(datos.Email) || !EmailValidator.EsValido(datos.Email.Trim()))
        {
            throw BusinessException.Validation("Email inválido", "email_invalido");
        }

        if (string.IsNullOrEmpty(datos.Password) || datos.Password.Length < 8)
        {
            throw BusinessException.Validation("La contraseña debe tener al menos 8 caracteres", "password_corta");
        }
    }

    private static string? MimeToExtension(string mime) => mime?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/svg+xml" => ".svg",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => null,
    };
}
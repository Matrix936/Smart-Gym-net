using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class SociosService : ISociosService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly ISociosRepository _socios;
    private readonly IBitacoraAuditoriaRepository _bitacora;
    private readonly ISedeResolutionService _sedeResolution;

    public SociosService(
        IAuthService auth,
        IAuthorizationService authz,
        ISociosRepository socios,
        IBitacoraAuditoriaRepository bitacora,
        ISedeResolutionService sedeResolution)
    {
        _auth = auth;
        _authz = authz;
        _socios = socios;
        _bitacora = bitacora;
        _sedeResolution = sedeResolution;
    }

    public async Task<Socio> CrearSocioAsync(string token, CrearSocioDatos datos, long? idSedeFrontend = null, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.SociosCrear, ct);

        ValidarNombre(datos.Nombre);
        if (datos.Email is not null && !EmailValidator.EsValido(datos.Email))
        {
            throw BusinessException.Validation("Email inválido", "email_invalido");
        }

        // Resolución de id_sede: la sesión local gana sobre el id_sede enviado.
        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);

        var ahora = DateHelper.NowIsoUtc();
        var socio = new Socio
        {
            IdSocio = UuidHelper.NewV4(),
            Nombre = datos.Nombre.Trim(),
            ApellidoPaterno = datos.ApellidoPaterno ?? string.Empty,
            ApellidoMaterno = datos.ApellidoMaterno ?? string.Empty,
            Email = datos.Email?.Trim(),
            Telefono = datos.Telefono?.Trim(),
            FechaNacimiento = datos.FechaNacimiento,
            FotoPath = datos.FotoPath,
            ContactoEmergenciaNombre = datos.ContactoEmergenciaNombre,
            ContactoEmergenciaTelefono = datos.ContactoEmergenciaTelefono,
            IdSedeRegistro = idSede,
            Estado = SocioEstados.Activo,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };

        await _socios.CrearConBitacoraAsync(socio, RegistrarBitacora(info, "socio.creado", socio.IdSocio, idSede, null, null), ct);

        return socio;
    }

    public async Task<Socio> ObtenerSocioAsync(string token, string idSocio, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        return await _socios.GetByIdAsync(idSocio, ct)
            ?? throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");
    }

    public async Task<PagedResult<Socio>> BuscarAsync(string token, string? query = null, string? estado = null, int pagina = 1, int tamanoPagina = TamanosPagina.Default, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);

        string? estadoFiltro = null;
        if (!string.IsNullOrWhiteSpace(estado))
        {
            estadoFiltro = estado.Trim().ToLowerInvariant();
            if (!SocioEstados.EsValido(estadoFiltro))
            {
                throw BusinessException.Validation($"Estado inválido: {estado}. Valores permitidos: {string.Join(", ", SocioEstados.Validos)}.", "estado_invalido");
            }
        }

        return await _socios.SearchAsync(
            string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
            estadoFiltro,
            pagina,
            tamanoPagina,
            ct);
    }

    public async Task<Socio> ActualizarSocioAsync(string token, ActualizarSocioDatos datos, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.SociosEditar, ct);

        var existente = await _socios.GetByIdAsync(datos.IdSocio, ct)
            ?? throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");

        ValidarNombre(datos.Nombre);
        if (datos.Email is not null && !EmailValidator.EsValido(datos.Email))
        {
            throw BusinessException.Validation("Email inválido", "email_invalido");
        }

        // Preserva id_socio e id_sede_registro (el UPDATE no los toca).
        await _socios.ActualizarConBitacoraAsync(
            new Socio
            {
                IdSocio = existente.IdSocio,
                Nombre = datos.Nombre.Trim(),
                ApellidoPaterno = datos.ApellidoPaterno ?? string.Empty,
                ApellidoMaterno = datos.ApellidoMaterno ?? string.Empty,
                Email = datos.Email?.Trim(),
                Telefono = datos.Telefono?.Trim(),
                FechaNacimiento = datos.FechaNacimiento,
                FotoPath = datos.FotoPath,
                ContactoEmergenciaNombre = datos.ContactoEmergenciaNombre,
                ContactoEmergenciaTelefono = datos.ContactoEmergenciaTelefono,
            },
            RegistrarBitacora(info, "socio.editado", existente.IdSocio, info.IdSede, null, null),
            ct);

        return (await _socios.GetByIdAsync(existente.IdSocio, ct))!;
    }

    public async Task CambiarEstadoAsync(string token, string idSocio, string estadoNuevo, string? motivo = null, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.SociosEditar, ct);

        if (!SocioEstados.EsValido(estadoNuevo))
        {
            throw BusinessException.Validation("Estado de socio inválido", "estado_socio_invalido");
        }

        var existente = await _socios.GetByIdAsync(idSocio, ct)
            ?? throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");

        var ahora = DateHelper.NowIsoUtc();
        var historial = new SocioHistorialEstado
        {
            Id = UuidHelper.NewV4(),
            IdSocio = idSocio,
            EstadoAnterior = existente.Estado,
            EstadoNuevo = estadoNuevo,
            Motivo = motivo,
            IdUsuario = info.IdUsuario,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };

        await _socios.CambiarEstadoConBitacoraAsync(
            idSocio,
            estadoNuevo,
            historial,
            RegistrarBitacora(info, "socio.estado_cambiado", idSocio, info.IdSede, existente.Estado, estadoNuevo),
            ct);
    }

    public async Task EliminarSocioAsync(string token, string idSocio, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.SociosEliminar, ct);

        var existente = await _socios.GetByIdAsync(idSocio, ct)
            ?? throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");

        await _socios.SoftDeleteAsync(idSocio, ct);
        await _bitacora.InsertAsync(
            RegistrarBitacora(info, "socio.eliminado", idSocio, existente.IdSedeRegistro, existente.Estado, null),
            ct);
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw BusinessException.Validation("El nombre es obligatorio", "nombre_vacio");
        }
    }

    private static BitacoraAuditoria RegistrarBitacora(SessionInfo info, string accion, string idRegistro, long? idSede, string? anterior, string? nuevo) =>
        new()
        {
            IdRegistro = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            Accion = accion,
            TablaAfectada = "socios",
            IdRegistroAfectado = idRegistro,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            IdSede = idSede ?? info.IdSede,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        };
}
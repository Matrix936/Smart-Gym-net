using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

/// <summary>
/// Catálogo de maquinaria/equipo físico por sede. Mismo patrón que
/// ProductosService: sesión + permiso en cada operación y bitácora en toda
/// escritura (para no repetir el gap que hubo con planes/productos).
/// </summary>
public sealed class MaquinariaService : IMaquinariaService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IMaquinariaRepository _maquinaria;
    private readonly ISedeResolutionService _sedeResolution;
    private readonly IBitacoraAuditoriaRepository _bitacora;

    public MaquinariaService(
        IAuthService auth,
        IAuthorizationService authz,
        IMaquinariaRepository maquinaria,
        ISedeResolutionService sedeResolution,
        IBitacoraAuditoriaRepository bitacora)
    {
        _auth = auth;
        _authz = authz;
        _maquinaria = maquinaria;
        _sedeResolution = sedeResolution;
        _bitacora = bitacora;
    }

    public async Task<PagedResult<Maquina>> BuscarAsync(
        string token,
        string? nombre = null,
        string? estado = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        bool? esActivo = null,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MaquinariaGestionar, ct);
        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);

        return await _maquinaria.SearchAsync(idSede, nombre, estado, pagina, tamanoPagina, esActivo, ct);
    }

    public async Task<Maquina> CrearAsync(
        string token,
        string nombre,
        string? descripcion,
        string estado,
        string? notas,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MaquinariaGestionar, ct);

        ValidarDatos(nombre);
        ValidarEstado(estado);

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
        var maquina = new Maquina
        {
            IdMaquina = UuidHelper.NewV4(),
            Nombre = nombre.Trim(),
            Descripcion = Normalizar(descripcion),
            Estado = estado.Trim(),
            IdSede = idSede,
            Notas = Normalizar(notas),
            EsActivo = true,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        };

        await _maquinaria.InsertAsync(maquina, ct);
        await RegistrarBitacora(info, "maquina.creada", maquina.IdMaquina, idSede,
            null, $"nombre:{maquina.Nombre}|estado:{maquina.Estado}");
        return maquina;
    }

    public async Task<Maquina> EditarAsync(
        string token,
        string idMaquina,
        string nombre,
        string? descripcion,
        string? notas,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MaquinariaGestionar, ct);

        ValidarDatos(nombre);

        var maquina = await ObtenerCualquierEstadoAsync(idMaquina, ct);
        var anterior = $"nombre:{maquina.Nombre}";

        maquina.Nombre = nombre.Trim();
        maquina.Descripcion = Normalizar(descripcion);
        maquina.Notas = Normalizar(notas);
        maquina.UpdatedAt = DateHelper.NowIsoUtc();

        await _maquinaria.UpdateAsync(maquina, ct);
        await RegistrarBitacora(info, "maquina.editada", maquina.IdMaquina, maquina.IdSede,
            anterior, $"nombre:{maquina.Nombre}");
        return maquina;
    }

    public async Task<Maquina> CambiarEstadoAsync(string token, string idMaquina, string estadoNuevo, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MaquinariaGestionar, ct);

        ValidarEstado(estadoNuevo);

        var maquina = await ObtenerCualquierEstadoAsync(idMaquina, ct);
        if (maquina.Estado == estadoNuevo)
        {
            throw BusinessException.Validation("La máquina ya está en ese estado", "mismo_estado");
        }

        var anterior = maquina.Estado;
        maquina.Estado = estadoNuevo.Trim();
        maquina.UpdatedAt = DateHelper.NowIsoUtc();

        await _maquinaria.UpdateAsync(maquina, ct);
        await RegistrarBitacora(info, "maquina.estado_cambiado", maquina.IdMaquina, maquina.IdSede,
            anterior, maquina.Estado);
        return maquina;
    }

    public async Task DesactivarAsync(string token, string idMaquina, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MaquinariaGestionar, ct);

        var maquina = await ObtenerCualquierEstadoAsync(idMaquina, ct);
        await _maquinaria.DesactivarAsync(idMaquina, DateHelper.NowIsoUtc(), ct);
        await RegistrarBitacora(info, "maquina.desactivada", idMaquina, maquina.IdSede, "activo", "inactivo");
    }

    public async Task ActivarAsync(string token, string idMaquina, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MaquinariaGestionar, ct);

        var maquina = await ObtenerCualquierEstadoAsync(idMaquina, ct);
        await _maquinaria.ActivarAsync(idMaquina, DateHelper.NowIsoUtc(), ct);
        await RegistrarBitacora(info, "maquina.activada", idMaquina, maquina.IdSede, "inactivo", "activo");
    }

    private async Task<Maquina> ObtenerCualquierEstadoAsync(string idMaquina, CancellationToken ct)
    {
        return await _maquinaria.GetByIdCualquierEstadoAsync(idMaquina, ct)
            ?? throw BusinessException.NotFound("Máquina no encontrada", "maquina_no_encontrada");
    }

    private static void ValidarDatos(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw BusinessException.Validation("El nombre es obligatorio", "nombre_obligatorio");
        }
    }

    private static void ValidarEstado(string estado)
    {
        if (string.IsNullOrWhiteSpace(estado) || !MaquinaEstados.Validos.Contains(estado.Trim()))
        {
            throw BusinessException.Validation("Estado de máquina inválido", "estado_invalido");
        }
    }

    private static string? Normalizar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private async Task RegistrarBitacora(
        SessionInfo info,
        string accion,
        string idMaquina,
        long idSede,
        string? anterior,
        string? nuevo) =>
        await _bitacora.InsertAsync(new BitacoraAuditoria
        {
            IdRegistro = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            Accion = accion,
            TablaAfectada = "maquinaria",
            IdRegistroAfectado = idMaquina,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            IdSede = idSede,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
}

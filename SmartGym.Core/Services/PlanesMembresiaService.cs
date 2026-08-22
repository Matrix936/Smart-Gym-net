using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class PlanesMembresiaService : IPlanesMembresiaService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IPlanesMembresiaRepository _planes;
    private readonly IBitacoraAuditoriaRepository _bitacora;

    public PlanesMembresiaService(
        IAuthService auth,
        IAuthorizationService authz,
        IPlanesMembresiaRepository planes,
        IBitacoraAuditoriaRepository bitacora)
    {
        _auth = auth;
        _authz = authz;
        _planes = planes;
        _bitacora = bitacora;
    }

    public async Task<PagedResult<PlanMembresia>> BuscarAsync(
        string token, string? query = null, int pagina = 1, int tamanoPagina = TamanosPagina.Default, bool? esActivo = null, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        return await _planes.SearchAsync(query, pagina, tamanoPagina, esActivo, ct);
    }

    public async Task<PlanMembresia> CrearAsync(
        string token,
        string nombre,
        string? descripcion,
        int diasVigencia,
        int diasCongelamientoMax,
        long precioCentavos,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MembresiasCrear, ct);

        ValidarDatos(nombre, diasVigencia, diasCongelamientoMax, precioCentavos);

        var plan = new PlanMembresia
        {
            Nombre = nombre.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim(),
            DiasVigencia = diasVigencia,
            DiasCongelamientoMax = diasCongelamientoMax,
            PrecioCentavos = precioCentavos,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        };

        plan.IdPlan = await _planes.InsertAsync(plan, ct);
        await _bitacora.InsertAsync(RegistrarBitacora(
            info, "plan.creado", plan.IdPlan.ToString(),
            null, $"nombre:{plan.Nombre}|precio:{plan.PrecioCentavos}"), ct);
        return plan;
    }

    public async Task<PlanMembresia> EditarAsync(
        string token,
        long idPlan,
        string nombre,
        string? descripcion,
        int diasVigencia,
        int diasCongelamientoMax,
        long precioCentavos,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MembresiasCrear, ct);

        ValidarDatos(nombre, diasVigencia, diasCongelamientoMax, precioCentavos);

        var existente = await _planes.GetByIdAsync(idPlan, ct)
            ?? throw BusinessException.NotFound("Plan no encontrado", "plan_no_encontrado");

        var anterior = $"nombre:{existente.Nombre}|precio:{existente.PrecioCentavos}";

        existente.Nombre = nombre.Trim();
        existente.Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        existente.DiasVigencia = diasVigencia;
        existente.DiasCongelamientoMax = diasCongelamientoMax;
        existente.PrecioCentavos = precioCentavos;
        existente.UpdatedAt = DateHelper.NowIsoUtc();

        await _planes.UpdateAsync(existente, ct);
        await _bitacora.InsertAsync(RegistrarBitacora(
            info, "plan.editado", existente.IdPlan.ToString(),
            anterior, $"nombre:{existente.Nombre}|precio:{existente.PrecioCentavos}"), ct);
        return existente;
    }

    public async Task DesactivarAsync(string token, long idPlan, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MembresiasCrear, ct);

        await _planes.DesactivarAsync(idPlan, DateHelper.NowIsoUtc(), ct);
        await _bitacora.InsertAsync(RegistrarBitacora(
            info, "plan.desactivado", idPlan.ToString(), "activo", "inactivo"), ct);
    }

    public async Task ActivarAsync(string token, long idPlan, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MembresiasCrear, ct);

        await _planes.ActivarAsync(idPlan, DateHelper.NowIsoUtc(), ct);
        await _bitacora.InsertAsync(RegistrarBitacora(
            info, "plan.activado", idPlan.ToString(), "inactivo", "activo"), ct);
    }

    private static BitacoraAuditoria RegistrarBitacora(
        SessionInfo info,
        string accion,
        string idRegistro,
        string? anterior,
        string? nuevo) =>
        new()
        {
            IdRegistro = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            Accion = accion,
            TablaAfectada = "planes_membresia",
            IdRegistroAfectado = idRegistro,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            IdSede = info.IdSede,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        };

    private static void ValidarDatos(string nombre, int diasVigencia, int diasCongelamientoMax, long precioCentavos)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw BusinessException.Validation("El nombre es obligatorio", "nombre_obligatorio");
        }
        if (diasVigencia <= 0)
        {
            throw BusinessException.Validation("Los días de vigencia deben ser mayores a cero", "dias_vigencia_invalido");
        }
        if (diasCongelamientoMax < 0)
        {
            throw BusinessException.Validation("Los días de congelamiento no pueden ser negativos", "dias_congelamiento_invalido");
        }
        if (precioCentavos < 0)
        {
            throw BusinessException.Validation("El precio no puede ser negativo", "precio_invalido");
        }
    }
}

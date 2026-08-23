using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public interface IFinanzasService
{
    /// <summary>
    /// Dashboard de Finanzas: resumen del rango elegido, comparación contra el
    /// periodo anterior equivalente (mismos días, inmediatamente antes) y
    /// métricas de membresías. Solo lectura.
    /// </summary>
    Task<FinanzasDashboardDto> ObtenerDashboardAsync(
        string token,
        string desdeIso,
        string hastaIso,
        long? idSedeFrontend = null,
        CancellationToken ct = default);
}

public sealed class FinanzasService : IFinanzasService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IFinanzasRepository _finanzas;
    private readonly ISedeResolutionService _sedeResolution;

    public FinanzasService(
        IAuthService auth,
        IAuthorizationService authz,
        IFinanzasRepository finanzas,
        ISedeResolutionService sedeResolution)
    {
        _auth = auth;
        _authz = authz;
        _finanzas = finanzas;
        _sedeResolution = sedeResolution;
    }

    public async Task<FinanzasDashboardDto> ObtenerDashboardAsync(
        string token,
        string desdeIso,
        string hastaIso,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.FinanzasVer, ct);
        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);

        var actual = await _finanzas.ObtenerResumenAsync(idSede, desdeIso, hastaIso, ct);

        // Periodo anterior: mismo número de días, inmediatamente antes.
        var desde = DateHelper.ParseIsoUtc(desdeIso);
        var hasta = DateHelper.ParseIsoUtc(hastaIso);
        var duracion = hasta - desde;
        var anteriorDesde = ToIso(DateHelper.ParseIsoUtc(desdeIso).AddTicks(-duracion.Ticks - 1));
        var anteriorHasta = ToIso(desde.AddMilliseconds(-1));

        var anterior = await _finanzas.ObtenerResumenAsync(idSede, anteriorDesde, anteriorHasta, ct);

        // Socios activos: fuente de verdad = MembresiaEstadoCalculator (misma
        // que Kiosco/Accesos), no un recálculo en SQL. Conteo por socio distinto
        // (un socio puede tener más de una membresía activa).
        var membresias = await _finanzas.GetMembresiasPorSedeAsync(idSede, ct);
        var idsSociosActivos = new HashSet<string>();
        foreach (var m in membresias)
        {
            if (MembresiaEstadoCalculator.EstadoEfectivo(m) == MembresiaEstados.Activa)
            {
                idsSociosActivos.Add(m.IdSocio);
            }
        }

        return new FinanzasDashboardDto
        {
            Actual = actual,
            IngresosPeriodoAnterior = anterior.IngresosCentavos,
            EgresosPeriodoAnterior = anterior.EgresosCentavos,
            NetoPeriodoAnterior = anterior.NetoCentavos,
            SociosActivos = idsSociosActivos.Count,
            MembresiasNuevas = await _finanzas.ContarNuevasAsync(idSede, desdeIso, hastaIso, ct),
        };
    }

    private static string ToIso(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}

using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

/// <summary>
/// Agregaciones del Dashboard (/): resumen financiero con selector
/// Hoy/Semana/Mes (reutilizando IFinanzasRepository, misma fuente de dinero que
/// /finanzas), afluencia de accesos por hora local y candidatos a recordatorio
/// de membresía vía WhatsApp. Todos los listados usan el resolver OPCIONAL:
/// null = todas las sedes. Escrituras: ninguna aquí.
/// </summary>
public interface IDashboardService
{
    /// <summary>Rangos válidos para el selector del resumen.</summary>
    Task<DashboardResumenDto> ObtenerResumenAsync(
        string token, string rango, long? idSedeFrontend = null, CancellationToken ct = default);

    /// <summary>Accesos concedidos agrupados por hora local (0-23) de los últimos 30 días.</summary>
    Task<IReadOnlyList<AfluenciaHoraDto>> ObtenerAfluenciaAsync(
        string token, long? idSedeFrontend = null, CancellationToken ct = default);

    /// <summary>
    /// Socios a contactar: por vencer en ≤ 7 días o vencida hace ≤ 30 días,
    /// una fila por socio (la más urgente). Requiere teléfono cargado.
    /// </summary>
    Task<IReadOnlyList<RecordatorioMembresiaDto>> ObtenerRecordatoriosAsync(
        string token, long? idSedeFrontend = null, CancellationToken ct = default);

    /// <summary>Plantillas WhatsApp (por_vencer / vencida) desde configuración, con defaults.</summary>
    Task<(string PorVencer, string Vencida)> ObtenerPlantillasWhatsAppAsync(CancellationToken ct = default);

    /// <summary>Guarda las plantillas (vacío/espacios = volver al default).</summary>
    Task GuardarPlantillasWhatsAppAsync(
        string token, string porVencer, string vencida, CancellationToken ct = default);
}

public sealed class DashboardService : IDashboardService
{
    public const int DiasPorVencer = 7;
    public const int DiasVencidaMax = 30;

    public const string ClavePlantillaPorVencer = "whatsapp.plantilla.por_vencer";
    public const string ClavePlantillaVencida = "whatsapp.plantilla.vencida";

    public const string PlantillaPorVencerDefault =
        "Hola {nombre}, te recordamos que tu membres\u00eda de Smart Gym vence en {dias} d\u00eda(s). \u00a1Renueva y no pierdas tu entrenamiento!";
    public const string PlantillaVencidaDefault =
        "Hola {nombre}, tu membres\u00eda venci\u00f3 hace {dias} d\u00eda(s). \u00a1Ven a reactivarla y sigue entrenando con nosotros!";

    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IFinanzasRepository _finanzas;
    private readonly IDashboardRepository _dashboard;
    private readonly IConfiguracionRepository _configuracion;
    private readonly ISedeResolutionService _sedeResolution;

    public DashboardService(
        IAuthService auth,
        IAuthorizationService authz,
        IFinanzasRepository finanzas,
        IDashboardRepository dashboard,
        IConfiguracionRepository configuracion,
        ISedeResolutionService sedeResolution)
    {
        _auth = auth;
        _authz = authz;
        _finanzas = finanzas;
        _dashboard = dashboard;
        _configuracion = configuracion;
        _sedeResolution = sedeResolution;
    }

    public async Task<DashboardResumenDto> ObtenerResumenAsync(
        string token, string rango, long? idSedeFrontend = null, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.FinanzasVer, ct);
        var idSede = await _sedeResolution.ResolverIdSedeOpcionalAsync(info, idSedeFrontend, ct);

        var (desdeUtc, hastaUtc) = RangoLocal(rango);
        var duracion = hastaUtc - desdeUtc;

        var actual = await _finanzas.ObtenerResumenAsync(idSede, ToIso(desdeUtc), ToIso(hastaUtc), ct);

        // Periodo anterior equivalente: misma duración inmediatamente antes
        // (mismo cálculo que FinanzasService).
        var anteriorDesde = desdeUtc.AddTicks(-duracion.Ticks);
        var anteriorHasta = hastaUtc.AddTicks(-duracion.Ticks);
        var anterior = await _finanzas.ObtenerResumenAsync(idSede, ToIso(anteriorDesde), ToIso(anteriorHasta), ct);

        return new DashboardResumenDto
        {
            IngresosCentavos = actual.IngresosCentavos,
            EgresosCentavos = actual.EgresosCentavos,
            NetoCentavos = actual.NetoCentavos,
            IngresosPeriodoAnteriorCentavos = anterior.IngresosCentavos,
        };
    }

    public async Task<IReadOnlyList<AfluenciaHoraDto>> ObtenerAfluenciaAsync(
        string token, long? idSedeFrontend = null, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.AccesoVerBitacora, ct);
        var idSede = await _sedeResolution.ResolverIdSedeOpcionalAsync(info, idSedeFrontend, ct);

        var desdeUtc = DateTime.UtcNow.AddDays(-30);
        var timestamps = await _dashboard.ObtenerAccesosConcedidosAsync(idSede, ToIso(desdeUtc), ct);

        // El timestamp vive en UTC; la hora que le importa al negocio es LOCAL.
        var buckets = new int[24];
        foreach (var ts in timestamps)
        {
            if (!DateTimeOffset.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dto))
            {
                continue;
            }
            buckets[dto.ToLocalTime().Hour]++;
        }

        return Enumerable.Range(0, 24)
            .Select(h => new AfluenciaHoraDto { Hora = h, Total = buckets[h] })
            .ToList();
    }

    public async Task<IReadOnlyList<RecordatorioMembresiaDto>> ObtenerRecordatoriosAsync(
        string token, long? idSedeFrontend = null, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MembresiasVer, ct);
        var idSede = await _sedeResolution.ResolverIdSedeOpcionalAsync(info, idSedeFrontend, ct);

        var hoy = DateTime.UtcNow.Date;
        var candidatos = new List<(RecordatorioMembresiaDto Dto, int Prioridad, int AbsDias)>();

        foreach (var fila in await _dashboard.ObtenerActivasConSocioAsync(idSede, ct))
        {
            var finDate = DateHelper.ParseIsoUtc(fila.FechaFin).Date;

            // Estado efectivo con LA fuente de verdad (misma que Kiosco/Finanzas):
            // solo 'activa' puede estar por vencer o degradar a vencida.
            var estado = MembresiaEstadoCalculator.EstadoEfectivo(new Membresia
            {
                Estado = MembresiaEstados.Activa,
                FechaFin = fila.FechaFin,
            });

            var diasRestantes = (finDate - hoy).Days;
            RecordatorioMembresiaDto? dto = estado switch
            {
                MembresiaEstados.Activa when diasRestantes >= 0 && diasRestantes <= DiasPorVencer => new()
                {
                    IdSocio = fila.IdSocio,
                    NombreSocio = fila.NombreSocio,
                    Telefono = fila.Telefono,
                    Categoria = RecordatorioCategorias.PorVencer,
                    Dias = diasRestantes,
                },
                MembresiaEstados.Vencida when diasRestantes < 0 && -diasRestantes <= DiasVencidaMax => new()
                {
                    IdSocio = fila.IdSocio,
                    NombreSocio = fila.NombreSocio,
                    Telefono = fila.Telefono,
                    Categoria = RecordatorioCategorias.Vencida,
                    Dias = -diasRestantes,
                },
                _ => null,
            };

            if (dto is not null)
            {
                candidatos.Add((dto, dto.Categoria == RecordatorioCategorias.PorVencer ? 0 : 1, Math.Abs(diasRestantes)));
            }
        }

        // Una fila por socio: gana la más urgente (por vencer antes que vencida;
        // dentro de la misma categoría, la más cercana a la fecha crítica).
        return candidatos
            .OrderBy(c => c.Prioridad)
            .ThenBy(c => c.AbsDias)
            .DistinctBy(c => c.Dto.IdSocio)
            .Select(c => c.Dto)
            .ToList();
    }

    public async Task<(string PorVencer, string Vencida)> ObtenerPlantillasWhatsAppAsync(CancellationToken ct = default)
    {
        var porVencer = await _configuracion.GetAsync(ClavePlantillaPorVencer, ct);
        var vencida = await _configuracion.GetAsync(ClavePlantillaVencida, ct);
        return (
            string.IsNullOrWhiteSpace(porVencer) ? PlantillaPorVencerDefault : porVencer,
            string.IsNullOrWhiteSpace(vencida) ? PlantillaVencidaDefault : vencida);
    }

    public async Task GuardarPlantillasWhatsAppAsync(
        string token, string porVencer, string vencida, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.ConfiguracionEditar, ct);
        // Vacío/espacios = volver al default (Obtener cae al default si es nulo/blanco).
        await _configuracion.SetAsync(ClavePlantillaPorVencer,
            string.IsNullOrWhiteSpace(porVencer) ? null : porVencer.Trim(), ct);
        await _configuracion.SetAsync(ClavePlantillaVencida,
            string.IsNullOrWhiteSpace(vencida) ? null : vencida.Trim(), ct);
    }

    /// <summary>Rango [inicio, fin_exclusivo) en UTC según el día LOCAL del negocio.</summary>
    private static (DateTimeOffset DesdeUtc, DateTimeOffset HastaUtc) RangoLocal(string rango)
    {
        var hoy = DateTime.Today;
        var (inicioLocal, finLocal) = rango switch
        {
            "semana" => (hoy.AddDays(-6), hoy.AddDays(1)),
            "mes" => (new DateTime(hoy.Year, hoy.Month, 1), hoy.AddDays(1)),
            _ => (hoy, hoy.AddDays(1)), // "hoy"
        };
        return (new DateTimeOffset(inicioLocal), new DateTimeOffset(finLocal));
    }

    private static string ToIso(DateTimeOffset utc) =>
        utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}

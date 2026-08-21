using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Tests.Membresias;

/// <summary>
/// MembresiaEstadoCalculator: lógica pura extraída del filtro SQL que vivía
/// inline en AccesosRepository (ver auditoría SOLID / preparación para
/// MembresiasPage). "Vencida" nunca se persiste — se calcula comparando
/// fecha_fin contra hoy, y solo si el estado guardado es "activa".
/// </summary>
public sealed class MembresiaEstadoCalculatorTests
{
    private static readonly DateTime Hoy = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    private static Membresia Membresia(string estado, string fechaFin) => new()
    {
        IdMembresia = "m1",
        IdSocio = "s1",
        IdPlan = 1,
        IdSede = 1,
        FechaInicio = DateHelper.ToIsoUtc(Hoy.AddDays(-30)),
        FechaFin = fechaFin,
        Estado = estado,
        CreatedAt = DateHelper.NowIsoUtc(),
        UpdatedAt = DateHelper.NowIsoUtc(),
    };

    [Fact]
    public void activa_con_fecha_fin_pasada_se_calcula_como_vencida()
    {
        var m = Membresia(MembresiaEstados.Activa, DateHelper.ToIsoUtc(Hoy.AddDays(-1)));

        Assert.Equal(MembresiaEstados.Vencida, MembresiaEstadoCalculator.EstadoEfectivo(m, Hoy));
    }

    [Fact]
    public void activa_con_fecha_fin_futura_se_mantiene_activa()
    {
        var m = Membresia(MembresiaEstados.Activa, DateHelper.ToIsoUtc(Hoy.AddDays(10)));

        Assert.Equal(MembresiaEstados.Activa, MembresiaEstadoCalculator.EstadoEfectivo(m, Hoy));
    }

    [Fact]
    public void activa_con_fecha_fin_igual_a_hoy_se_mantiene_activa()
    {
        var m = Membresia(MembresiaEstados.Activa, DateHelper.ToIsoUtc(Hoy));

        Assert.Equal(MembresiaEstados.Activa, MembresiaEstadoCalculator.EstadoEfectivo(m, Hoy));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void congelada_nunca_se_sobreescribe_por_fecha(int diasFechaFin)
    {
        var m = Membresia(MembresiaEstados.Congelada, DateHelper.ToIsoUtc(Hoy.AddDays(diasFechaFin)));

        Assert.Equal(MembresiaEstados.Congelada, MembresiaEstadoCalculator.EstadoEfectivo(m, Hoy));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void cancelada_nunca_se_sobreescribe_por_fecha(int diasFechaFin)
    {
        var m = Membresia(MembresiaEstados.Cancelada, DateHelper.ToIsoUtc(Hoy.AddDays(diasFechaFin)));

        Assert.Equal(MembresiaEstados.Cancelada, MembresiaEstadoCalculator.EstadoEfectivo(m, Hoy));
    }

    [Fact]
    public void vencida_persistida_se_mantiene_vencida_sin_importar_fecha_fin()
    {
        // Caso defensivo: si algo persistiera "vencida" directamente (hoy nada lo
        // hace), no debe recalcularse a activa aunque la fecha_fin sea futura —
        // solo "activa" es candidata a degradar por fecha.
        var m = Membresia(MembresiaEstados.Vencida, DateHelper.ToIsoUtc(Hoy.AddDays(30)));

        Assert.Equal(MembresiaEstados.Vencida, MembresiaEstadoCalculator.EstadoEfectivo(m, Hoy));
    }
}

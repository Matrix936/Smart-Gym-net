using SmartGym.Core.Entities;
using SmartGym.Core.Errors;

namespace SmartGym.Tests.Accesos;

/// <summary>
/// Matriz de comportamiento de AccesoDecisor (lógica pura extraída de
/// AccesosRepository — ver auditoría SOLID: el switch original dejaba caer
/// SocioEstados.Suspendido en el default y lo trataba como activo). Cubre los
/// 4 estados de SocioEstados.Validos, cruzados con membresía activa/congelada/
/// vencida donde aplica, antes de que Kiosco dependa de este resultado.
/// </summary>
public sealed class AccesoDecisorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(MembresiaEstados.Activa)]
    [InlineData(MembresiaEstados.Congelada)]
    public void socio_bloqueado_deniega_sin_importar_la_membresia(string? estadoMembresia)
    {
        var decision = AccesoDecisor.Decidir(SocioEstados.Bloqueado, estadoMembresia);

        Assert.Equal(AccesoEstados.Denegado, decision.Estado);
        Assert.Equal(MotivosDenegacionAcceso.SocioBloqueado, decision.MotivoDenegacion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(MembresiaEstados.Activa)]
    [InlineData(MembresiaEstados.Congelada)]
    public void socio_inactivo_deniega_sin_importar_la_membresia(string? estadoMembresia)
    {
        var decision = AccesoDecisor.Decidir(SocioEstados.Inactivo, estadoMembresia);

        Assert.Equal(AccesoEstados.Denegado, decision.Estado);
        Assert.Equal(MotivosDenegacionAcceso.SocioInactivo, decision.MotivoDenegacion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(MembresiaEstados.Activa)]
    [InlineData(MembresiaEstados.Congelada)]
    public void socio_suspendido_deniega_sin_importar_la_membresia(string? estadoMembresia)
    {
        // Caso que motivó esta extracción: antes caía en el default del switch
        // y se trataba igual que un socio activo.
        var decision = AccesoDecisor.Decidir(SocioEstados.Suspendido, estadoMembresia);

        Assert.Equal(AccesoEstados.Denegado, decision.Estado);
        Assert.Equal(MotivosDenegacionAcceso.SocioSuspendido, decision.MotivoDenegacion);
    }

    [Fact]
    public void socio_activo_con_membresia_activa_concede_sin_motivo()
    {
        var decision = AccesoDecisor.Decidir(SocioEstados.Activo, MembresiaEstados.Activa);

        Assert.Equal(AccesoEstados.Concedido, decision.Estado);
        Assert.Null(decision.MotivoDenegacion);
    }

    [Fact]
    public void socio_activo_con_membresia_congelada_deniega()
    {
        var decision = AccesoDecisor.Decidir(SocioEstados.Activo, MembresiaEstados.Congelada);

        Assert.Equal(AccesoEstados.Denegado, decision.Estado);
        Assert.Equal(MotivosDenegacionAcceso.MembresiaCongelada, decision.MotivoDenegacion);
    }

    [Fact]
    public void socio_activo_sin_membresia_vigente_deniega_por_vencida()
    {
        // null == ninguna fila activa/congelada con fecha_fin >= hoy (la consulta
        // ya filtra eso) — vencida o inexistente, mismo resultado de negocio.
        var decision = AccesoDecisor.Decidir(SocioEstados.Activo, null);

        Assert.Equal(AccesoEstados.Denegado, decision.Estado);
        Assert.Equal(MotivosDenegacionAcceso.MembresiaVencida, decision.MotivoDenegacion);
    }

    [Fact]
    public void estado_de_socio_no_reconocido_lanza_error_explicito_no_aprueba_implicitamente()
    {
        var ex = Assert.Throws<BusinessException>(
            () => AccesoDecisor.Decidir("estado-fantasma", MembresiaEstados.Activa));

        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("estado_socio_invalido", ex.Code);
    }
}

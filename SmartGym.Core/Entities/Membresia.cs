using SmartGym.Core.Common;

namespace SmartGym.Core.Entities;

/// <summary>Estados válidos de una membresía.</summary>
public static class MembresiaEstados
{
    public const string Activa = "activa";
    public const string Vencida = "vencida";
    public const string Congelada = "congelada";
    public const string Cancelada = "cancelada";

    public static bool EsValido(string estado) => estado is Activa or Vencida or Congelada or Cancelada;
}

/// <summary>
/// "Vencida" nunca se escribe en membresias.estado — ninguna operación del sistema
/// transiciona una membresía activa a vencida, se calcula al vuelo comparando
/// fecha_fin contra hoy (antes vivía inline en AccesosRepository como filtro SQL;
/// se extrae aquí para que cualquier otro consumidor, ej. una futura pantalla de
/// listado de membresías, use la misma fuente de verdad en vez de reimplementarlo).
/// </summary>
public static class MembresiaEstadoCalculator
{
    /// <summary>
    /// Estado real de la membresía en este momento. Congelada y cancelada son
    /// estados terminales/explícitos que una fecha nunca sobreescribe — solo una
    /// membresía activa puede degradar a vencida por fecha_fin pasada.
    /// </summary>
    public static string EstadoEfectivo(Membresia membresia, DateTime? ahora = null)
    {
        if (membresia.Estado != MembresiaEstados.Activa)
        {
            return membresia.Estado;
        }

        var hoy = (ahora ?? DateTime.UtcNow).Date;
        var fin = DateHelper.ParseIsoUtc(membresia.FechaFin).Date;
        return fin >= hoy ? MembresiaEstados.Activa : MembresiaEstados.Vencida;
    }
}

/// <summary>membresias — id UUID v4. fecha_fin se calcula server-side desde el plan.</summary>
public sealed class Membresia
{
    public string IdMembresia { get; set; } = string.Empty;
    public string IdSocio { get; set; } = string.Empty;
    public long IdPlan { get; set; }
    public long IdSede { get; set; }
    public string FechaInicio { get; set; } = string.Empty;
    public string FechaFin { get; set; } = string.Empty;
    public string? FechaCancelacion { get; set; }
    public string Estado { get; set; } = MembresiaEstados.Activa;
    public long? IdVendedor { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
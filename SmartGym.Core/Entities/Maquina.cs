namespace SmartGym.Core.Entities;

/// <summary>Estados operativos de maquinaria (equipo físico por sede).</summary>
public static class MaquinaEstados
{
    public const string Funcionando = "funcionando";
    public const string EnMantenimiento = "en_mantenimiento";
    public const string FueraDeServicio = "fuera_de_servicio";

    public static readonly IReadOnlyList<string> Validos = [Funcionando, EnMantenimiento, FueraDeServicio];
}

/// <summary>maquinaria — equipo físico del gimnasio por sede. No es stock vendible.</summary>
public sealed class Maquina
{
    public string IdMaquina { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string Estado { get; set; } = MaquinaEstados.Funcionando;
    public long IdSede { get; set; }
    public string? Notas { get; set; }
    public bool EsActivo { get; set; } = true;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

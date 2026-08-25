namespace SmartGym.Core.Common;

/// <summary>Agregacion de accesos concedidos agrupados por dia de la semana.</summary>
public sealed class AfluenciaDiaDto
{
    /// <summary>Etiqueta legible: "Lun", "Mar", ... "Dom".</summary>
    public string Etiqueta { get; init; } = string.Empty;
    public int Total { get; init; }
}
namespace SmartGym.Core.Common;

/// <summary>Resultado paginado genérico: página actual + total de registros (sin LIMIT) para que la UI calcule el total de páginas.</summary>
public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalRegistros { get; init; }
    public required int Pagina { get; init; }
    public required int TamanoPagina { get; init; }

    public int TotalPaginas => TamanoPagina <= 0 ? 0 : (int)Math.Ceiling(TotalRegistros / (double)TamanoPagina);
}

/// <summary>Tamaños de página permitidos en listados paginados (Miembros y, a futuro, Membresías/Caja).</summary>
public static class TamanosPagina
{
    public const int Diez = 10;
    public const int VeinteYCinco = 25;
    public const int Cincuenta = 50;

    public static readonly IReadOnlyList<int> Validos = [Diez, VeinteYCinco, Cincuenta];

    public const int Default = VeinteYCinco;

    public static bool EsValido(int tamanoPagina) => Validos.Contains(tamanoPagina);
}

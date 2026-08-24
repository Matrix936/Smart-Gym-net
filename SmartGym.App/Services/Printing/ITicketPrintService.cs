namespace SmartGym.App.Services;

/// <summary>
/// Transporte de tickets térmicos: listado de impresoras del sistema, envío
/// RAW (ESC/POS) al spooler de Windows y rasterización del logo. La generación
/// de comandos vive aparte en Core (TicketEscposBuilder, puro y testeado).
/// </summary>
public interface ITicketPrintService
{
    /// <summary>Impresoras instaladas en Windows (equivalente a Get-Printer).</summary>
    Task<IReadOnlyList<string>> ObtenerImpresorasInstaladasAsync();

    /// <summary>
    /// Envía bytes crudos al spooler con DataType=RAW (winspool.drv). Lanza
    /// BusinessException con mensaje claro si la impresora no está disponible.
    /// </summary>
    Task ImprimirRawAsync(string impresora, byte[] datos, string nombreDocumento = "Smart Gym Ticket");

    /// <summary>
    /// Convierte un logo (PNG/JPG/WEBP) a raster monocromo ESC/POS (GS v 0).
    /// Devuelve null si el formato no es decodificable (ej. SVG en v1): el
    /// ticket se imprime sin logo, no es un error.
    /// </summary>
    Task<byte[]?> RasterizarLogoAsync(byte[] imagenBytes, int papelAncho, int anchoMaximoDots);
}

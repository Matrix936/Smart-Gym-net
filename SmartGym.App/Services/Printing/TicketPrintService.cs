using System.Runtime.InteropServices;
using System.Drawing.Printing;
using SkiaSharp;
using SmartGym.Core.Errors;

namespace SmartGym.App.Services;

/// <summary>
/// Implementación Windows del transporte de tickets: winspool.drv con
/// DataType=RAW (mismo mecanismo que ferre-pos vía FFI), listado vía
/// PrinterSettings (WinForms, ya referenciado como shared framework) y
/// rasterización del logo con SkiaSharp.
/// </summary>
public sealed class TicketPrintService : ITicketPrintService
{
    public Task<IReadOnlyList<string>> ObtenerImpresorasInstaladasAsync()
    {
#if WINDOWS
        var impresoras = new List<string>();
        foreach (string nombre in PrinterSettings.InstalledPrinters)
        {
            impresoras.Add(nombre);
        }
        impresoras.Sort(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlyList<string>>(impresoras);
#else
        return Task.FromResult<IReadOnlyList<string>>([]);
#endif
    }

    public Task ImprimirRawAsync(string impresora, byte[] datos, string nombreDocumento = "Smart Gym Ticket")
    {
        ArgumentNullException.ThrowIfNull(datos);
#if WINDOWS
        if (string.IsNullOrWhiteSpace(impresora))
        {
            throw BusinessException.Validation("No hay impresora de tickets configurada.", "impresora_no_configurada");
        }
        var nombre = impresora.Trim();
        return Task.Run(() => EnviarRaw(nombre, datos, nombreDocumento));
#else
        return Task.FromException(
            new PlatformNotSupportedException("La impresión de tickets solo está disponible en Windows."));
#endif
    }

    public Task<byte[]?> RasterizarLogoAsync(byte[] imagenBytes, int papelAncho, int anchoMaximoDots)
    {
        ArgumentNullException.ThrowIfNull(imagenBytes);
        if (imagenBytes.Length == 0)
        {
            return Task.FromResult<byte[]?>(null);
        }

        // Puntos de impresión según ancho de papel (203 dpi típico en térmicas).
        var puntosPapel = papelAncho switch
        {
            32 => 384,
            48 => 576,
            _ => 504,
        };
        var anchoObjetivo = Math.Clamp(anchoMaximoDots, 160, Math.Min(576, puntosPapel));

        return Task.Run<byte[]?>(() =>
        {
            using var bitmap = SKBitmap.Decode(imagenBytes);
            if (bitmap is null)
            {
                // Formato no decodificable (ej. SVG): el ticket sale sin logo.
                return null;
            }

            var escala = Math.Min((float)anchoObjetivo / bitmap.Width, 1f);
            var ancho = Math.Max(1, (int)MathF.Round(bitmap.Width * escala));
            var alto = Math.Max(1, (int)MathF.Round(bitmap.Height * escala));
            var bytesPorFila = (ancho + 7) / 8;

            var raster = new byte[bytesPorFila * alto];
            for (var y = 0; y < alto; y++)
            {
                var origenY = Math.Min(bitmap.Height - 1, y * bitmap.Height / alto);
                for (var xByte = 0; xByte < bytesPorFila; xByte++)
                {
                    var byteActual = 0;
                    for (var bit = 0; bit < 8; bit++)
                    {
                        var x = xByte * 8 + bit;
                        if (x >= ancho)
                        {
                            continue;
                        }
                        var origenX = Math.Min(bitmap.Width - 1, x * bitmap.Width / ancho);
                        var color = bitmap.GetPixel(origenX, origenY);
                        // Mezcla sobre fondo blanco antes del umbral (logos con transparencia).
                        var alfa = color.Alpha / 255f;
                        var luminancia =
                            (color.Red * 0.299f + color.Green * 0.587f + color.Blue * 0.114f) * alfa +
                            255f * (1f - alfa);
                        if (luminancia < 180)
                        {
                            byteActual |= 0x80 >> bit;
                        }
                    }
                    raster[y * bytesPorFila + xByte] = (byte)byteActual;
                }
            }

            // GS v 0: raster estándar, columnas/alto little-endian.
            var salida = new List<byte>(raster.Length + 8)
            {
                0x1D, 0x76, 0x30, 0x00,
                (byte)(bytesPorFila & 0xFF),
                (byte)((bytesPorFila >> 8) & 0xFF),
                (byte)(alto & 0xFF),
                (byte)((alto >> 8) & 0xFF),
            };
            salida.AddRange(raster);
            return salida.ToArray();
        });
    }

#if WINDOWS
    private static void EnviarRaw(string impresora, byte[] datos, string nombreDocumento)
    {
        if (!OpenPrinter(impresora, out var handle, IntPtr.Zero))
        {
            throw BusinessException.Conflict(
                $"La impresora '{impresora}' no está conectada o no se encuentra disponible.",
                "impresora_no_disponible");
        }

        try
        {
            var documento = new DOC_INFO_1 { DocName = nombreDocumento, DataType = "RAW" };
            if (!StartDocPrinter(handle, 1, ref documento))
            {
                throw BusinessException.Conflict(
                    "Windows rechazó el documento RAW de impresión.", "impresion_rechazada");
            }

            try
            {
                if (!StartPagePrinter(handle))
                {
                    throw BusinessException.Conflict(
                        "Windows no pudo iniciar la página RAW de impresión.", "impresion_rechazada");
                }

                var puntero = Marshal.AllocHGlobal(datos.Length);
                try
                {
                    Marshal.Copy(datos, 0, puntero, datos.Length);
                    if (!WritePrinter(handle, puntero, datos.Length, out var escritos) || escritos != datos.Length)
                    {
                        throw BusinessException.Conflict(
                            "Windows no pudo enviar todos los bytes RAW a la impresora.",
                            "impresion_incompleta");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(puntero);
                }
                EndPagePrinter(handle);
            }
            finally
            {
                EndDocPrinter(handle);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        public string? DocName;
        public string? OutputFile;
        public string? DataType;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 pDocInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
#endif
}

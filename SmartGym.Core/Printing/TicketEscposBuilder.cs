using System.Globalization;
using System.Text;

namespace SmartGym.Core.Printing;

/// <summary>Partida de ticket: descripción con wrap y línea "cant x $precio ... $importe".</summary>
public sealed record TicketItemInput(
    string Descripcion,
    long Cantidad,
    long PrecioUnitarioCentavos,
    long ImporteCentavos);

/// <summary>
/// Payload del ticket térmico. Espejo del recibo visual de POS: empresa/sede,
/// folio (= IdVenta), socio opcional, método de pago, items server-side y saldo
/// pendiente cuando la venta quedó a crédito.
/// </summary>
public sealed record TicketPayloadInput
{
    public required string Folio { get; init; }
    public required string FechaTexto { get; init; }
    public string? EmpresaNombre { get; init; }
    public string? Rfc { get; init; }
    public required string SedeNombre { get; init; }
    public string? Direccion { get; init; }
    public string? Telefono { get; init; }
    public required string Cajero { get; init; }
    public string? Cliente { get; init; }
    public required string MetodoPago { get; init; }
    public string Estado { get; init; } = "completada";
    public required IReadOnlyList<TicketItemInput> Items { get; init; }
    public required long TotalCentavos { get; init; }

    /// <summary>>0 solo en ventas a crédito: imprime Pago recibido / Saldo / Vence.</summary>
    public long SaldoPendienteCentavos { get; init; }
    public string? VenceTexto { get; init; }

    /// <summary>Efectivo capturado en caja (solo método efectivo): imprime recibido/cambio.</summary>
    public long? EfectivoRecibidoCentavos { get; init; }

    /// <summary>Cambio calculado (recibido - total); se imprime junto al recibido.</summary>
    public long? CambioCentavos { get; init; }
    public string? MensajePie { get; init; }

    /// <summary>Raster monocromo ya preparado (GS v 0), sin comandos alrededor.</summary>
    public byte[]? LogoRaster { get; init; }

    /// <summary>Ancho de papel en caracteres: 32, 42 o 48.</summary>
    public int PapelAncho { get; init; } = 42;

    /// <summary>Densidad de impresión (GS E): 0–3.</summary>
    public int Densidad { get; init; } = 2;

    /// <summary>Kick de cajón monedero al inicio (solo tiene sentido en efectivo).</summary>
    public bool AbrirCajon { get; init; }
}

/// <summary>
/// Builder puro de tickets ESC/POS: payload → bytes para enviar RAW al spooler
/// de Windows. Sin I/O ni dependencias — el transporte vive en App
/// (RawPrinterHelper) y esto es directamente testeable. Patrón tomado de
/// ferre-pos (printing.rs): CP850, wraps por ancho de papel, dos columnas,
/// bloque de crédito con firma y corte parcial.
/// </summary>
public static class TicketEscposBuilder
{
    public const int MaxItems = 500;
    public const int MaxLogoBytes = 512 * 1024;
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;

    public static byte[] Build(TicketPayloadInput ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var width = ticket.PapelAncho switch
        {
            32 or 42 or 48 => ticket.PapelAncho,
            _ => throw new ArgumentException("El ancho de papel debe ser 32, 42 o 48 caracteres.", nameof(ticket)),
        };
        if (ticket.Items.Count == 0)
        {
            throw new ArgumentException("El ticket no contiene productos.", nameof(ticket));
        }
        if (ticket.Items.Count > MaxItems)
        {
            throw new ArgumentException($"El ticket excede el máximo de {MaxItems} partidas imprimibles.", nameof(ticket));
        }
        foreach (var item in ticket.Items)
        {
            if (item.Cantidad <= 0 || string.IsNullOrWhiteSpace(item.Descripcion)
                || item.PrecioUnitarioCentavos < 0 || item.ImporteCentavos < 0
                || item.PrecioUnitarioCentavos * item.Cantidad != item.ImporteCentavos)
            {
                throw new ArgumentException("El ticket contiene partidas inválidas.", nameof(ticket));
            }
        }
        if (ticket.TotalCentavos < 0 || ticket.SaldoPendienteCentavos < 0)
        {
            throw new ArgumentException("El ticket contiene montos inválidos.", nameof(ticket));
        }
        if (ticket.LogoRaster is { Length: > MaxLogoBytes })
        {
            throw new ArgumentException("El logotipo del ticket excede el límite de 512 KB.", nameof(ticket));
        }

        var separator = new string('-', width);
        var buffer = new List<byte>();

        if (ticket.AbrirCajon)
        {
            // ESC p 0: kick de cajón monedero.
            buffer.AddRange((byte[])[(byte)Esc, 0x70, 0x00, 0x19, 0xFA]);
        }

        // Init, codepage CP850, doble golpe, densidad, fuente normal.
        buffer.AddRange((byte[])[(byte)Esc, 0x40]);
        buffer.AddRange((byte[])[(byte)Esc, 0x74, 0x02]);
        buffer.AddRange((byte[])[(byte)Esc, 0x47, 0x01]);
        buffer.AddRange((byte[])[Gs, 0x45, (byte)Math.Clamp(ticket.Densidad, 0, 3)]);
        buffer.AddRange((byte[])[(byte)Esc, 0x21, 0x00]);

        // ---- Encabezado centrado ------------------------------------------------
        Center(buffer, on: true);
        if (ticket.LogoRaster is { Length: > 0 } logo)
        {
            buffer.AddRange(logo);
            buffer.Add(0x0A);
        }
        Line(buffer, Fit(Optional(ticket.EmpresaNombre, "SMART GYM"), width));
        Line(buffer, Fit($"RFC: {Optional(ticket.Rfc, "SIN RFC CONFIGURADO")}", width));
        Line(buffer, Fit(ticket.SedeNombre.Trim(), width));

        // Folio corto (primeros 8 chars): cabe completo en cualquier ancho de
        // papel sin truncar, y es suficiente para el buscador parcial (LIKE)
        // de /ventas. El UUID completo (36 + prefijo = 43 cols) ni siquiera
        // cabía en 42 columnas y Fit() lo cortaba en algo ilegible.
        Line(buffer, $"FOLIO: {FolioCorto(ticket.Folio)}");
        Line(buffer, separator);
        Center(buffer, on: false);

        // ---- Meta -----------------------------------------------------------------
        Line(buffer, TwoColumns("Fecha", ticket.FechaTexto, width));
        Line(buffer, TwoColumns("Cajero", ticket.Cajero, width));
        if (!string.IsNullOrWhiteSpace(ticket.Cliente))
        {
            foreach (var linea in TwoColumnsWrapped("Cliente", ticket.Cliente.Trim(), width))
            {
                Line(buffer, linea);
            }
        }
        Line(buffer, TwoColumns("Pago", ticket.MetodoPago.ToUpperInvariant(), width));
        if (!string.IsNullOrWhiteSpace(ticket.Estado))
        {
            Line(buffer, TwoColumns("Estado", ticket.Estado, width));
        }
        Line(buffer, separator);

        // ---- Partidas ---------------------------------------------------------------
        foreach (var item in ticket.Items)
        {
            foreach (var linea in Wrap(item.Descripcion.ToUpperInvariant(), width))
            {
                Line(buffer, linea);
            }
            Line(buffer, TwoColumns($"{item.Cantidad} x {Money(item.PrecioUnitarioCentavos)}", Money(item.ImporteCentavos), width));
        }
        Line(buffer, separator);

        // ---- Totales ------------------------------------------------------------------
        if (ticket.SaldoPendienteCentavos > 0)
        {
            Line(buffer, TwoColumns("Pago recibido", Money(ticket.TotalCentavos - ticket.SaldoPendienteCentavos), width));
            Line(buffer, TwoColumns("Saldo pendiente", Money(ticket.SaldoPendienteCentavos), width));
            if (!string.IsNullOrWhiteSpace(ticket.VenceTexto))
            {
                Line(buffer, TwoColumns("Vence", ticket.VenceTexto.Trim(), width));
            }
        }
        Line(buffer, TwoColumns("TOTAL", Money(ticket.TotalCentavos), width));
        if (ticket.EfectivoRecibidoCentavos is not null)
        {
            Line(buffer, TwoColumns("Efectivo recibido", Money(ticket.EfectivoRecibidoCentavos.Value), width));
            if (ticket.CambioCentavos is not null)
            {
                Line(buffer, TwoColumns("Cambio", Money(ticket.CambioCentavos.Value), width));
            }
        }

        // ---- Bloque de crédito ----------------------------------------------------------
        if (EsCredito(ticket.MetodoPago))
        {
            Line(buffer, separator);
            foreach (var linea in Wrap(
                "AVISO DE CREDITO: El saldo pendiente debe cubrirse antes de la fecha " +
                "limite indicada. Conserve este ticket para registrar su abono.", width))
            {
                Line(buffer, linea);
            }
            Line(buffer, "");
            Center(buffer, on: true);
            Line(buffer, new string('_', Math.Min(width, 34)));
            Line(buffer, "       Firma de Conformidad");
            Center(buffer, on: false);
        }

        // ---- Pie --------------------------------------------------------------------------
        Line(buffer, separator);
        Center(buffer, on: true);
        if (!string.IsNullOrWhiteSpace(ticket.MensajePie))
        {
            Line(buffer, Fit(ticket.MensajePie.Trim(), width));
        }
        if (!string.IsNullOrWhiteSpace(ticket.Direccion))
        {
            foreach (var linea in Wrap(ticket.Direccion.Trim(), width))
            {
                Line(buffer, linea);
            }
        }
        if (!string.IsNullOrWhiteSpace(ticket.Telefono))
        {
            Line(buffer, Fit($"Tel.: {ticket.Telefono.Trim()}", width));
        }
        Center(buffer, on: false);

        // Feed y corte parcial.
        buffer.AddRange([0x0A, 0x0A, 0x0A]);
        buffer.AddRange((byte[])[Gs, 0x56, 0x42, 0x00]);

        return [.. buffer];
    }

    /// <summary>True si el método de pago es crédito (bloque aviso + firma).</summary>
    public static bool EsCredito(string metodoPago) =>
        string.Equals(metodoPago?.Trim(), "credito", StringComparison.OrdinalIgnoreCase);

    /// <summary>Monto como "$0.00" invariante (sin separador de miles: las columnas cuadran).</summary>
    public static string Money(long centavos) =>
        "$" + (centavos / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- helpers

    private static void Line(List<byte> buffer, string texto)
    {
        buffer.AddRange(Cp850(texto));
        buffer.Add(0x0A);
    }

    private static void Center(List<byte> buffer, bool on) =>
        buffer.AddRange((byte[])[(byte)Esc, 0x61, on ? (byte)0x01 : (byte)0x00]);

    private static string Optional(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    /// <summary>Primeros 8 caracteres del folio, seguro con folios más cortos.</summary>
    private static string FolioCorto(string folio)
    {
        var f = folio.Trim();
        return f.Length <= 8 ? f : f[..8];
    }

    /// <summary>Trunca con "..." al ancho exacto (una sola línea).</summary>
    private static string Fit(string value, int width)    {
        var text = value.Replace("\n", " ").Trim();
        if (text.Length <= width)
        {
            return text;
        }
        return text[..Math.Max(0, width - 3)] + "...";
    }

    /// <summary>Wrap por palabras; palabras más largas que el ancho se parten.</summary>
    private static List<string> Wrap(string value, int width)
    {
        var normalized = string.Join(' ', value.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var lines = new List<string>();
        if (normalized.Length == 0)
        {
            return lines;
        }

        var current = string.Empty;
        foreach (var word in normalized.Split(' '))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (candidate.Length <= width)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0)
            {
                lines.Add(current);
                current = string.Empty;
            }
            if (word.Length <= width)
            {
                current = word;
                continue;
            }
            for (var start = 0; start < word.Length; start += width)
            {
                lines.Add(word[start..Math.Min(start + width, word.Length)]);
            }
        }
        if (current.Length > 0)
        {
            lines.Add(current);
        }
        return lines;
    }

    private static string TwoColumns(string left, string right, int width)
    {
        var maxLeft = Math.Max(0, width - right.Length - 1);
        left = Fit(left, maxLeft);
        var padding = Math.Max(1, width - left.Length - right.Length);
        return left + new string(' ', padding) + right;
    }

    /// <summary>Etiqueta fija a la izquierda con valor largo envuelto; continuas al margen derecho.</summary>
    private static List<string> TwoColumnsWrapped(string left, string value, int width)
    {
        var valueWidth = Math.Max(1, width - left.Length - 1);
        var wrapped = Wrap(value, valueWidth);
        var lines = new List<string>();
        if (wrapped.Count == 0)
        {
            return lines;
        }

        lines.Add(TwoColumns(left, wrapped[0], width));
        foreach (var continuation in wrapped.Skip(1))
        {
            lines.Add(new string(' ', width - continuation.Length) + continuation);
        }
        return lines;
    }

    /// <summary>
    /// Tabla CP850 (latín-1) para impresoras térmicas: los textos del gym usan
    /// acentos/español; cualquier carácter fuera de tabla se degrada a espacio.
    /// </summary>
    private static byte[] Cp850(string texto)
    {
        var bytes = new byte[texto.Length];
        for (var i = 0; i < texto.Length; i++)
        {
            bytes[i] = texto[i] switch
            {
                'á' => 0xA0, 'é' => 0x82, 'í' => 0xA1, 'ó' => 0xA2, 'ú' => 0xA3,
                'Á' => 0xB5, 'É' => 0x90, 'Í' => 0xD6, 'Ó' => 0xE0, 'Ú' => 0xE9,
                'ñ' => 0xA4, 'Ñ' => 0xA5,
                'ü' => 0x81, 'Ü' => 0x9A,
                '¿' => 0xA8, '¡' => 0xAD,
                '°' => 0xF8,
                'ç' => 0x87, 'Ç' => 0x80,
                '´' => 0xEF,
                '¨' => 0xF9,
                '¹' => 0xFC, '²' => 0xFD, '³' => 0xFE,
                var ch when ch < 128 => (byte)ch,
                _ => 0x20,
            };
        }
        return bytes;
    }
}

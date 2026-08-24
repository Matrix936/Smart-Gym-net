using System.Globalization;
using SmartGym.Core.Entities;
using SmartGym.Core.Printing;

namespace SmartGym.App.Services;

/// <summary>Datos del negocio para el encabezado/pie del ticket (empresa + sede).</summary>
public sealed record TicketDatosNegocio(
    string? Empresa,
    string? Rfc,
    string Sede,
    string? Direccion,
    string? Telefono,
    string Cajero,
    string? MensajePie);

/// <summary>
/// Mapeo venta → payload de ticket, compartido por el recibo del POS (impresión
/// automática o manual) y la reimpresión desde /ventas. La generación de bytes
/// vive en Core (TicketEscposBuilder); aquí solo se traducen entidades.
/// </summary>
public static class TicketVentaMapper
{
    public static TicketPayloadInput DesdeVenta(
        string folio,
        string fechaTexto,
        string metodoPago,
        string estado,
        string? socio,
        IReadOnlyList<DetalleVentaInfo> items,
        long totalCentavos,
        long saldoPendienteCentavos,
        string? venceTexto,
        TicketDatosNegocio negocio,
        byte[]? logoRaster,
        int papelAncho,
        int densidad,
        bool abrirCajon)
    {
        return DesdeVenta(
            folio, fechaTexto, metodoPago, estado, socio,
            items.Select(i => new TicketItemInput(
                i.DescripcionProducto ?? $"#{i.IdProducto}",
                i.Cantidad,
                i.PrecioUnitarioCentavos,
                i.SubtotalCentavos)).ToList(),
            totalCentavos, saldoPendienteCentavos, venceTexto, negocio, logoRaster, papelAncho, densidad, abrirCajon);
    }

    public static TicketPayloadInput DesdeVenta(
        string folio,
        string fechaTexto,
        string metodoPago,
        string estado,
        string? socio,
        IReadOnlyList<TicketItemInput> items,
        long totalCentavos,
        long saldoPendienteCentavos,
        string? venceTexto,
        TicketDatosNegocio negocio,
        byte[]? logoRaster,
        int papelAncho,
        int densidad,
        bool abrirCajon)
    {
        return new TicketPayloadInput
        {
            Folio = folio,
            FechaTexto = fechaTexto,
            EmpresaNombre = negocio.Empresa,
            Rfc = negocio.Rfc,
            SedeNombre = negocio.Sede,
            Direccion = negocio.Direccion,
            Telefono = negocio.Telefono,
            Cajero = negocio.Cajero,
            Cliente = socio,
            MetodoPago = metodoPago,
            Estado = estado,
            Items = items,
            TotalCentavos = totalCentavos,
            SaldoPendienteCentavos = saldoPendienteCentavos,
            VenceTexto = venceTexto,
            MensajePie = negocio.MensajePie,
            LogoRaster = logoRaster,
            PapelAncho = papelAncho,
            Densidad = densidad,

            // El switch de Configuración dice "cajón en ventas de efectivo".
            AbrirCajon = abrirCajon && string.Equals(metodoPago?.Trim(), "efectivo", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>dataUrl "data:image/png;base64,..." → bytes crudos; null si no aplica.</summary>
    public static byte[]? DecodificarDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return null;
        }

        var separador = dataUrl.IndexOf(";base64,", StringComparison.Ordinal);
        if (separador < 0)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(dataUrl[(separador + 8)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>ISO UTC → "dd/MM/yyyy HH:mm" local (formato del recibo en pantalla); null si no parsea.</summary>
    public static string? FormatearFechaIso(string? isoUtc)
    {
        if (string.IsNullOrWhiteSpace(isoUtc) ||
            !DateTimeOffset.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return null;
        }

        return dto.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-MX"));
    }
}

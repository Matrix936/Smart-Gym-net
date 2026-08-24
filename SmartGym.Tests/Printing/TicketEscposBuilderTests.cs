using System.Text;
using Xunit;
using SmartGym.Core.Printing;

namespace SmartGym.Tests.Printing;

/// <summary>
/// Builder puro de tickets ESC/POS: estructura de bytes, wraps, columnas y
/// bloques condicionales (cajón, saldo pendiente, crédito). No requiere
/// impresora — el transporte RAW se prueba aparte con hardware real.
/// </summary>
public sealed class TicketEscposBuilderTests
{
    private static TicketItemInput Item(string desc = "Proteina 1kg", long cantidad = 1, long precio = 50000) =>
        new(desc, cantidad, precio, cantidad * precio);

    private static TicketPayloadInput Payload(Func<TicketPayloadInput, TicketPayloadInput>? patch = null)
    {
        var payload = new TicketPayloadInput
        {
            Folio = "ABC-123",
            FechaTexto = "23/08/2026 18:30",
            EmpresaNombre = "Smart Gym",
            Rfc = "XAXX010101000",
            SedeNombre = "Matriz",
            Cajero = "Recepción",
            MetodoPago = "efectivo",
            Items = [Item()],
            TotalCentavos = 50000,
        };
        payload = patch?.Invoke(payload) ?? payload;
        return payload;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start = 0)
    {
        for (var i = start; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length && ok; j++)
            {
                ok = haystack[i + j] == needle[j];
            }
            if (ok)
            {
                return i;
            }
        }
        return -1;
    }

    [Fact]
    public void inicia_con_init_y_termina_con_corte_parcial()
    {
        var bytes = TicketEscposBuilder.Build(Payload());
        Assert.Equal(new byte[] { 0x1B, 0x40 }, bytes[..2]);
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, bytes[^4..]);
    }

    [Fact]
    public void kick_de_cajon_solo_cuando_abrir_cajon()
    {
        const byte esc = 0x1B;
        var conCajon = TicketEscposBuilder.Build(Payload(p => p with { AbrirCajon = true }));
        Assert.Equal(new byte[] { esc, 0x70, 0x00, 0x19, 0xFA }, conCajon[..5]);
        // El init sigue inmediatamente después del kick.
        Assert.Equal(new byte[] { esc, 0x40 }, conCajon[5..7]);

        var sinCajon = TicketEscposBuilder.Build(Payload());
        Assert.Equal(new byte[] { esc, 0x40 }, sinCajon[..2]);
    }

    [Fact]
    public void ancho_invalido_lanza_argumento()
    {
        foreach (var ancho in new[] { 0, 31, 33, 80 })
        {
            Assert.Throws<ArgumentException>(() => TicketEscposBuilder.Build(Payload(p => p with { PapelAncho = ancho })));
        }
    }

    [Fact]
    public void sin_items_lanza_argumento()
    {
        Assert.Throws<ArgumentException>(() => TicketEscposBuilder.Build(Payload(p => p with { Items = [] })));
    }

    [Fact]
    public void mas_de_500_items_lanza_argumento()
    {
        var items = Enumerable.Range(0, 501).Select(i => Item(desc: $"P{i}")).ToList();
        Assert.Throws<ArgumentException>(() => TicketEscposBuilder.Build(Payload(p => p with { Items = items })));
    }

    [Fact]
    public void importe_inconsistente_con_cantidad_precio_lanza()
    {
        var item = new TicketItemInput("Proteina", 2, 50000, ImporteCentavos: 99999);
        Assert.Throws<ArgumentException>(() => TicketEscposBuilder.Build(Payload(p => p with { Items = [item] })));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(42)]
    [InlineData(48)]
    public void ninguna_linea_de_texto_excede_el_ancho(int ancho)
    {
        var bytes = TicketEscposBuilder.Build(Payload(p => p with
        {
            PapelAncho = ancho,
            Items =
            [
                Item("Whey protein isolada sabor chocolate con stevia de calabaza ultra filtrada", 3, 125037),
                Item("Creatina", 1, 30000),
            ],
            SaldoPendienteCentavos = 150000,
            VenceTexto = "07/09/2026",
            Direccion = "Av. Siempre Viva 742, col. Centro, cerca del parque",
        }));

        var text = TextoAscii(bytes);
        foreach (var linea in text.Split('\n'))
        {
            Assert.True(linea.TrimEnd().Length <= ancho, $"Línea excede {ancho}: '{linea}'");
        }
    }

    [Fact]
    public void encabezado_trae_empresa_rfc_sede_y_folio()
    {
        var text = TextoAscii(TicketEscposBuilder.Build(Payload()));
        Assert.Contains("Smart Gym", text);
        Assert.Contains("RFC: XAXX010101000", text);
        Assert.Contains("Matriz", text);
        Assert.Contains("FOLIO: ABC-123", text);
    }

    [Fact]
    public void partida_lleva_descripcion_mayusculas_y_linea_cantidad_importe()
    {
        var text = TextoAscii(TicketEscposBuilder.Build(Payload()));
        Assert.Contains("PROTEINA 1KG", text);
        Assert.Contains("1 x $500.00", text);
        Assert.Contains("$500.00", text);
    }

    [Fact]
    public void total_alineado_al_final_de_su_linea()
    {
        var bytes = TicketEscposBuilder.Build(Payload());
        var text = TextoAscii(bytes);
        var lineaTotal = text.Split('\n').Single(l => l.StartsWith("TOTAL"));
        Assert.EndsWith("$500.00", lineaTotal.TrimEnd());
    }

    [Fact]
    public void acentos_se_codifican_cp850()
    {
        // "Proteína" → P r o t e í(0xA1) n a — la descripción va en mayúsculas.
        var bytes = TicketEscposBuilder.Build(Payload(p => p with
        {
            Items = [new TicketItemInput("Proteína", 1, 50000, 50000)],
        }));
        Assert.NotEqual(-1, IndexOf(bytes, [(byte)'P', (byte)'R', (byte)'O', (byte)'T', (byte)'E', 0xD6]));
    }

    [Fact]
    public void venta_de_contado_no_imprime_saldo_ni_bloque_credito()
    {
        var text = TextoAscii(TicketEscposBuilder.Build(Payload()));
        Assert.DoesNotContain("Saldo pendiente", text);
        Assert.DoesNotContain("Firma de Conformidad", text);
        Assert.Contains("TOTAL", text);
    }

    [Fact]
    public void efectivo_recibido_y_cambio_se_imprimen_cuando_presentes()
    {
        var bytes = TicketEscposBuilder.Build(Payload(p => p with
        {
            TotalCentavos = 50000,
            EfectivoRecibidoCentavos = 100000,
            CambioCentavos = 50000,
        }));
        var text = TextoAscii(bytes);
        Assert.Contains("Efectivo recibido", text);
        Assert.Contains("$1000.00", text); // Money sin separador de miles
        Assert.Contains("Cambio", text);
        // Orden: TOTAL antes que el efectivo recibido.
        Assert.True(text.IndexOf("TOTAL") < text.IndexOf("Efectivo recibido"));
    }

    [Fact]
    public void sin_efectivo_capturado_no_imprime_filas_de_recibido_ni_cambio()
    {
        var text = TextoAscii(TicketEscposBuilder.Build(Payload()));
        Assert.DoesNotContain("Efectivo recibido", text);
        Assert.DoesNotContain("Cambio", text);
    }

    [Fact]
    public void cambio_sin_efectivo_no_se_imprime()
    {
        var text = TextoAscii(TicketEscposBuilder.Build(Payload(p => p with { CambioCentavos = 100 })));
        Assert.DoesNotContain("Cambio", text);
    }

    [Fact]
    public void venta_a_credito_imprime_pagado_saldo_vence_firma_y_aviso()
    {
        var bytes = TicketEscposBuilder.Build(Payload(p => p with
        {
            MetodoPago = "credito",
            TotalCentavos = 50000,
            SaldoPendienteCentavos = 20000,
            VenceTexto = "07/09/2026",
        }));
        var text = TextoAscii(bytes);
        Assert.Contains("Pago recibido", text);
        Assert.Contains("$300.00", text);
        Assert.Contains("Saldo pendiente", text);
        Assert.Contains("$200.00", text);
        Assert.Contains("Vence", text);
        Assert.Contains("AVISO DE CREDITO", text);
        Assert.Contains("Firma de Conformidad", text);
    }

    [Fact]
    public void bloque_credito_va_por_metodo_no_por_saldo()
    {
        // Saldo pendiente sin método crédito (p. ej. flujo apartado futuro): firma no aplica.
        var text = TextoAscii(TicketEscposBuilder.Build(Payload(p => p with
        {
            MetodoPago = "efectivo",
            SaldoPendienteCentavos = 100,
            VenceTexto = "01/01/2027",
        })));
        Assert.Contains("Saldo pendiente", text);
        Assert.DoesNotContain("Firma de Conformidad", text);
    }

    [Fact]
    public void logo_raster_se_inserta_tras_el_comando_de_centro()
    {
        var logo = new byte[] { 0x1D, 0x76, 0x30, 0x00, 0x01, 0x00, 0x01, 0x00, 0xFF };
        var bytes = TicketEscposBuilder.Build(Payload(p => p with { LogoRaster = logo }));
        var centro = IndexOf(bytes, [0x1B, 0x61, 0x01]);
        Assert.NotEqual(-1, centro);
        Assert.Equal(centro + 3, IndexOf(bytes, logo));
    }

    [Fact]
    public void logo_demasiado_grande_lanza_argumento()
    {
        var logoGigante = new byte[512 * 1024 + 1];
        Assert.Throws<ArgumentException>(() => TicketEscposBuilder.Build(Payload(p => p with { LogoRaster = logoGigante })));
    }

    [Fact]
    public void pie_incluye_mensaje_direccion_y_telefono_centrados()
    {
        var bytes = TicketEscposBuilder.Build(Payload(p => p with
        {
            MensajePie = "¡Gracias por su compra!",
            Direccion = "Av. Reforma 100",
            Telefono = "951 000 0000",
        }));
        var text = TextoAscii(bytes);
        var lineas = text.Split('\n');
        Assert.Contains(lineas, l => l.Contains("Gracias por su compra"));
        Assert.Contains(lineas, l => l.Contains("Av. Reforma 100"));
        Assert.Contains(lineas, l => l.Contains("Tel.: 951 000 0000"));
    }

    /// <summary>
    /// Decodifica el buffer para aserciones de texto: salta los comandos
    /// ESC/GS conocidos del builder y mapea CP850 básico a ASCII.
    /// </summary>
    private static string TextoAscii(byte[] bytes)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < bytes.Length; i++)
        {
            if ((bytes[i] == 0x1B || bytes[i] == 0x1D) && i + 1 < bytes.Length)
            {
                // Largo total del comando según su segundo byte.
                i += bytes[i + 1] switch
                {
                    0x40 => 1,                      // ESC @   (init)
                    0x70 => 4,                      // ESC p n m (cajón)
                    0x45 => 2,                      // GS E n  (densidad)
                    0x56 => 3,                      // GS V m  (corte)
                    _ => 2,                         // ESC t / ESC G / ESC a / ESC ! (2-3 bytes)
                };
                continue;
            }
            sb.Append(bytes[i] switch
            {
                0x0A => '\n',
                >= 32 and < 127 => (char)bytes[i],
                _ => '?',
            });
        }
        return sb.ToString();
    }
}

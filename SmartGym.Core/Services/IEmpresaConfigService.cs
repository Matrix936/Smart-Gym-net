using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

public interface IEmpresaConfigService
{
    /// <summary>Empresa (fila única) + datos de contacto de la sede del usuario + logo actual.</summary>
    Task<(EmpresaConfigFiscal Empresa, Sede Sede, string? LogoDataUrl)> ObtenerAsync(
        string token, CancellationToken ct = default);

    /// <summary>Datos fiscales/comerciales de la empresa (nombre, razón social, RFC, régimen).</summary>
    Task<EmpresaConfigFiscal> ActualizarDatosAsync(string token, string nombreComercial,
        string? razonSocial, string? rfc, string? regimenFiscal, CancellationToken ct = default);

    /// <summary>Datos de contacto de la sede: dirección, teléfono y código postal.</summary>
    Task GuardarContactoSedeAsync(string token, string? direccion, string? telefono,
        string? codigoPostal, long? idSedeFrontend = null, CancellationToken ct = default);

    /// <summary>Guarda/sube un nuevo logo (reemplaza al anterior, limpia huérfanos).</summary>
    Task GuardarLogoAsync(string token, byte[] bytes, string mime, CancellationToken ct = default);

    /// <summary>Quita el logo actual.</summary>
    Task QuitarLogoAsync(string token, CancellationToken ct = default);

    /// <summary>Preferencia de impresora (clave/valor global; sin flujo de impresión todavía).</summary>
    Task GuardarImpresoraAsync(string token, string nombreImpresora, CancellationToken ct = default);

    Task<string?> ObtenerImpresoraAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Interruptor maestro pos.permite_credito: habilita ventas POS con pago
    /// incompleto (quedan en Cobranza). Apagado por defecto — opt-in del dueño.
    /// </summary>
    Task ActualizarPosPermiteCreditoAsync(string token, bool permite, CancellationToken ct = default);

    Task<bool> ObtenerPosPermiteCreditoAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Modalidad de registro de accesos del Kiosco (solo_entrada / entrada_y_salida).
    /// Lectura con sesión válida — la UI la necesita sin permiso de configuración.
    /// </summary>
    Task<string> ObtenerModoRegistroAccesoAsync(string token, CancellationToken ct = default);

    /// <summary>Guarda la modalidad (gate ConfiguracionEditar + bitácora). Rechaza valores inválidos.</summary>
    Task GuardarModoRegistroAccesoAsync(string token, string modo, CancellationToken ct = default);

    /// <summary>
    /// Estilo de la franja de promociones del Kiosco (tarjetas / cinta).
    /// Lectura con sesión válida — la UI la necesita sin permiso de configuración.
    /// </summary>
    Task<string> ObtenerEstiloPromocionesKioscoAsync(string token, CancellationToken ct = default);

    /// <summary>Guarda el estilo (gate ConfiguracionEditar + bitácora). Rechaza valores inválidos.</summary>
    Task GuardarEstiloPromocionesKioscoAsync(string token, string estilo, CancellationToken ct = default);

    /// <summary>
    /// Periféricos del POS (impresora de tickets, papel, densidad, cajón e
    /// impresión automática). Lectura con sesión válida — la UI del POS la
    /// necesita sin permiso de configuración.
    /// </summary>
    Task<PerifericosTicket> ObtenerPerifericosAsync(string token, CancellationToken ct = default);

    /// <summary>Guarda periféricos (gate ConfiguracionEditar + bitácora). Valores inválidos se normalizan al default.</summary>
    Task<PerifericosTicket> GuardarPerifericosAsync(string token, PerifericosTicket perifericos, CancellationToken ct = default);

    /// <summary>Renombra la sede principal (passthrough con gate de sesión/permiso).</summary>
    Task<string> RenombrarSedeAsync(string token, string nombre, CancellationToken ct = default);
}

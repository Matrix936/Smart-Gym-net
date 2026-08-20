namespace SmartGym.Core.Authorization;

/// <summary>
/// Catálogo de acciones (02-reglas-de-negocio.md §3). El listado vive en código
/// y el seed de SUPERADMIN lo inserta en el primer arranque (idempotente) —
/// agregar una acción es un cambio de código versionado, no una migración de datos.
/// </summary>
public static class PermisoCatalogo
{
    public const string SociosCrear = "socios.crear";
    public const string SociosEditar = "socios.editar";
    public const string SociosEliminar = "socios.eliminar";
    public const string MembresiasCrear = "membresias.crear";
    public const string MembresiasCongelar = "membresias.congelar";
    public const string MembresiasCancelar = "membresias.cancelar";
    public const string PosVender = "pos.vender";
    public const string PosCancelarVenta = "pos.cancelar_venta";
    public const string CajaAbrir = "caja.abrir";
    public const string CajaCerrar = "caja.cerrar";
    public const string CobranzaRegistrarAbono = "cobranza.registrar_abono";
    public const string ConfiguracionEditarPerifericos = "configuracion.editar_perifericos";
    public const string ConfiguracionGestionarUsuarios = "configuracion.gestionar_usuarios";
    public const string AccesoVerBitacora = "acceso.ver_bitacora";
    public const string AccesoForzarEntradaManual = "acceso.forzar_entrada_manual";

    /// <summary>Todas las acciones del sistema (seed de SUPERADMIN).</summary>
    public static IReadOnlyList<string> Todas() =>
    [
        SociosCrear, SociosEditar, SociosEliminar,
        MembresiasCrear, MembresiasCongelar, MembresiasCancelar,
        PosVender, PosCancelarVenta,
        CajaAbrir, CajaCerrar,
        CobranzaRegistrarAbono,
        ConfiguracionEditarPerifericos, ConfiguracionGestionarUsuarios,
        AccesoVerBitacora, AccesoForzarEntradaManual,
    ];
}
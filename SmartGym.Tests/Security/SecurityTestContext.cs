using SmartGym.Data.Db;
using SmartGym.Data.Repositories;
using SmartGym.Data.Storage;
using SmartGym.Core.Services;

namespace SmartGym.Tests.Security;

/// <summary>
/// Contexto por test: BD SQLite temporal + repos + servicios listos. Fase 2
/// requiere aislamiento (login/setup mutan el estado), por eso NO se comparte
/// la BD entre tests a diferencia de la colección "data".
/// </summary>
internal sealed class SecurityTestContext : IDisposable
{
    public string DbPath { get; }
    public string LogosDir { get; }

    public SessionState SessionState { get; } = new();

    public UsuariosRepository Usuarios { get; }
    public RolesRepository Roles { get; }
    public SedesRepository Sedes { get; }
    public PermisosRolRepository Permisos { get; }
    public EmpresaConfigFiscalRepository Empresa { get; }
    public SociosRepository Socios { get; }
    public BitacoraAuditoriaRepository Bitacora { get; }

    public PlanesMembresiaRepository Planes { get; }
    public CajasSesionesRepository Cajas { get; }
    public CajaMovimientosRepository Movimientos { get; }
    public MembresiasRepository Membresias { get; }
    public MembresiasPagosRepository Pagos { get; }
    public MembresiasCongelamientosRepository Congelamientos { get; }
    public CuentasCobrarRepository CuentasCobrar { get; }
    public CobrosRecordatoriosRepository Recordatorios { get; }

    public AccesosRepository Accesos { get; }
    public DispositivosAccesoRepository DispositivosAcceso { get; }
    public SociosBiometricosRepository SociosBiometricos { get; }

    public ProductosRepository Productos { get; }
    public InventarioSucursalRepository Inventario { get; }
    public VentasRepository Ventas { get; }
    public MaquinariaRepository Maquinaria { get; }
    public FinanzasRepository FinanzasRepo { get; }
    public LogoStorage LogoStorage { get; }
    public ConfiguracionRepository Configuracion { get; }

    public AuthService Auth { get; }
    public AuthorizationService Authz { get; }
    public SetupService Setup { get; }
    public SedeResolutionService SedeResolution { get; }
    public SociosService SociosService { get; }
    public CajaService CajaService { get; }
    public MembresiasService MembresiasService { get; }
    public PlanesMembresiaService PlanesService { get; }
    public AccesoService AccesoService { get; }
    public PosService PosService { get; }
    public CobranzaService CobranzaService { get; }
    public VentasService VentasService { get; }
    public ProductosService ProductosService { get; }
    public BitacoraService BitacoraService { get; }
    public MaquinariaService MaquinariaService { get; }
    public FinanzasService FinanzasService { get; }
    public EmpresaConfigService EmpresaConfigService { get; }
    public DashboardService DashboardService { get; }

    public SecurityTestContext()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"smart_gym_sec_{Guid.NewGuid():N}.db");
        LogosDir = Path.Combine(Path.GetTempPath(), $"smart_gym_logos_{Guid.NewGuid():N}");
        DbInitializer.Initialize(DbPath);

        var sesiones = new SesionesRepository(DbPath);
        var cuentas = new CuentasRecordadasRepository(DbPath);
        Configuracion = new ConfiguracionRepository(DbPath);
        var logo = new LogoStorage(LogosDir);

        Usuarios = new UsuariosRepository(DbPath);
        Roles = new RolesRepository(DbPath);
        Sedes = new SedesRepository(DbPath);
        Permisos = new PermisosRolRepository(DbPath);
        Empresa = new EmpresaConfigFiscalRepository(DbPath);
        Socios = new SociosRepository(DbPath);
        Bitacora = new BitacoraAuditoriaRepository(DbPath);
        Planes = new PlanesMembresiaRepository(DbPath);
        Cajas = new CajasSesionesRepository(DbPath);
        Movimientos = new CajaMovimientosRepository(DbPath);
        Membresias = new MembresiasRepository(DbPath);
        Pagos = new MembresiasPagosRepository(DbPath);
        Congelamientos = new MembresiasCongelamientosRepository(DbPath);
        CuentasCobrar = new CuentasCobrarRepository(DbPath);
        Recordatorios = new CobrosRecordatoriosRepository(DbPath);

        Accesos = new AccesosRepository(DbPath);
        DispositivosAcceso = new DispositivosAccesoRepository(DbPath);
        SociosBiometricos = new SociosBiometricosRepository(DbPath);

        Productos = new ProductosRepository(DbPath);
        Inventario = new InventarioSucursalRepository(DbPath);
        Ventas = new VentasRepository(DbPath);
        Maquinaria = new MaquinariaRepository(DbPath);
        FinanzasRepo = new FinanzasRepository(DbPath);
        LogoStorage = new LogoStorage(LogosDir);

        Auth = new AuthService(Usuarios, sesiones, cuentas, SessionState, new SesionStore(LogosDir), Bitacora);
        Authz = new AuthorizationService(Auth, Roles, Permisos);
        Setup = new SetupService(Usuarios, Roles, Empresa, Configuracion, Sedes, logo);
        SedeResolution = new SedeResolutionService(Sedes);
        SociosService = new SociosService(Auth, Authz, Socios, Bitacora, SedeResolution);
        // NOTA: firma actualizada para el CajaService en curso de otra línea de
        // trabajo (movimientos + bitácora); ajuste mecánico para que la suite compile.
        CajaService = new CajaService(Auth, Authz, Cajas, Movimientos, SedeResolution, Bitacora);
        MembresiasService = new MembresiasService(Auth, Authz, Socios, Planes, Cajas, Membresias, Bitacora, SedeResolution);
        PlanesService = new PlanesMembresiaService(Auth, Authz, Planes, Bitacora);
        AccesoService = new AccesoService(Auth, Authz, Accesos, SedeResolution);
        PosService = new PosService(Auth, Authz, Socios, Cajas, Productos, Inventario, Ventas,
            CuentasCobrar, Configuracion, Bitacora, SedeResolution);
        CobranzaService = new CobranzaService(Auth, Authz, Socios, Cajas, CuentasCobrar, Recordatorios, Bitacora, SedeResolution);
        VentasService = new VentasService(Auth, Authz, Movimientos, Ventas, Productos, SedeResolution);
        ProductosService = new ProductosService(Auth, Authz, Productos, Inventario, SedeResolution, Bitacora);
        BitacoraService = new BitacoraService(Auth, Authz, Bitacora, SedeResolution);
        MaquinariaService = new MaquinariaService(Auth, Authz, Maquinaria, SedeResolution, Bitacora);
        FinanzasService = new FinanzasService(Auth, Authz, FinanzasRepo, SedeResolution);
        EmpresaConfigService = new EmpresaConfigService(
            Auth, Authz, Empresa, Sedes, LogoStorage, Configuracion, Bitacora, SedeResolution);
        DashboardService = new DashboardService(
            Auth, Authz, FinanzasRepo,
            new SmartGym.Data.Repositories.DashboardRepository(DbPath),
            Configuracion, SedeResolution);
    }

    public void Dispose()
    {
    }
}
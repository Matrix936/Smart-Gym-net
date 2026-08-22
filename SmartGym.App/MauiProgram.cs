using SmartGym.Core.Biometrics;
using SmartGym.Core.Repositories;
using SmartGym.Core.Services;
using SmartGym.Data.Db;
using SmartGym.Data.Repositories;
using SmartGym.Data.Storage;
using SmartGym.App.Services;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using Plugin.Maui.Audio;
using System.Globalization;

namespace SmartGym.App;

public static class MauiProgram
{
public static MauiApp CreateMauiApp()
{
	// Convención del proyecto: formato México (punto decimal, $ MXN, dd/mm/yyyy).
	// Fijar ANTES de construir nada: los MudNumericField heredan la cultura del
	// hilo; sin esto, en equipos con otra configuración regional los campos de
	// precio esperan/esperaban coma y contradecían a los helpers es-MX de UI.
	var culturaMx = new CultureInfo("es-MX");
	CultureInfo.DefaultThreadCurrentCulture = culturaMx;
	CultureInfo.DefaultThreadCurrentUICulture = culturaMx;

	var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddMudServices();

		// ---- Infraestructura (Fase 1) ----
		// Datos de la app en AppData\Roaming\Smart-Gym-net (DB, logos, etc.).
		var dataDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Smart-Gym-net");
		Directory.CreateDirectory(dataDir);

		var dbPath = Path.Combine(dataDir, "smart_gym.db");
		DbInitializer.Initialize(dbPath);
		SeedPermisosSuperadmin(dbPath, dataDir);

		builder.Services.AddSingleton(dbPath);
		builder.Services.AddScoped<ILogoStorage>(_ => new LogoStorage(dataDir));
		builder.Services.AddScoped<ISesionStore>(_ => new SesionStore(dataDir));
		builder.Services.AddScoped<ITerminalConfigStore>(_ => new TerminalConfigStore(dataDir));

		builder.Services.AddScoped<ISedesRepository>(_ => new SedesRepository(dbPath));
		builder.Services.AddScoped<IRolesRepository>(_ => new RolesRepository(dbPath));
		builder.Services.AddScoped<IPermisosRolRepository>(_ => new PermisosRolRepository(dbPath));
		builder.Services.AddScoped<IUsuariosRepository>(_ => new UsuariosRepository(dbPath));
		builder.Services.AddScoped<ISesionesRepository>(_ => new SesionesRepository(dbPath));
		builder.Services.AddScoped<ICuentasRecordadasRepository>(_ => new CuentasRecordadasRepository(dbPath));
		builder.Services.AddScoped<IConfiguracionRepository>(_ => new ConfiguracionRepository(dbPath));
		builder.Services.AddScoped<IEmpresaConfigFiscalRepository>(_ => new EmpresaConfigFiscalRepository(dbPath));
		builder.Services.AddScoped<ISociosRepository>(_ => new SociosRepository(dbPath));
		builder.Services.AddScoped<IBitacoraAuditoriaRepository>(_ => new BitacoraAuditoriaRepository(dbPath));
		builder.Services.AddScoped<IPlanesMembresiaRepository>(_ => new PlanesMembresiaRepository(dbPath));
		builder.Services.AddScoped<ICajasSesionesRepository>(_ => new CajasSesionesRepository(dbPath));
		builder.Services.AddScoped<ICajaMovimientosRepository>(_ => new CajaMovimientosRepository(dbPath));
		builder.Services.AddScoped<IMembresiasRepository>(_ => new MembresiasRepository(dbPath));
		builder.Services.AddScoped<IMembresiasPagosRepository>(_ => new MembresiasPagosRepository(dbPath));
		builder.Services.AddScoped<IMembresiasCongelamientosRepository>(_ => new MembresiasCongelamientosRepository(dbPath));
		builder.Services.AddScoped<ICuentasCobrarRepository>(_ => new CuentasCobrarRepository(dbPath));
		builder.Services.AddScoped<IAccesosRepository>(_ => new AccesosRepository(dbPath));
		builder.Services.AddScoped<IDispositivosAccesoRepository>(_ => new DispositivosAccesoRepository(dbPath));
		builder.Services.AddScoped<ISociosBiometricosRepository>(_ => new SociosBiometricosRepository(dbPath));
		builder.Services.AddScoped<IProductosRepository>(_ => new ProductosRepository(dbPath));
		builder.Services.AddScoped<IMaquinariaRepository>(_ => new MaquinariaRepository(dbPath));
		builder.Services.AddScoped<IInventarioSucursalRepository>(_ => new InventarioSucursalRepository(dbPath));
		builder.Services.AddScoped<IVentasRepository>(_ => new VentasRepository(dbPath));
		builder.Services.AddScoped<ICobrosRecordatoriosRepository>(_ => new CobrosRecordatoriosRepository(dbPath));

		// ---- Servicios de dominio (Fase 2-5) ----
		builder.Services.AddScoped<ISessionState, SessionState>();
		builder.Services.AddScoped<IAuthService, AuthService>();
		builder.Services.AddScoped<IAuthorizationService, AuthorizationService>();
		builder.Services.AddScoped<ISetupService, SetupService>();
		builder.Services.AddScoped<IFeedbackService, FeedbackService>();
		builder.Services.AddScoped<IThemeState, ThemeState>();
		builder.Services.AddScoped<ISedeResolutionService, SedeResolutionService>();
		builder.Services.AddScoped<ISociosService, SociosService>();
		builder.Services.AddScoped<ICajaService, CajaService>();
		builder.Services.AddScoped<IMembresiasService, MembresiasService>();
		builder.Services.AddScoped<IPlanesMembresiaService, PlanesMembresiaService>();
		builder.Services.AddScoped<IAccesoService, AccesoService>();
		builder.Services.AddScoped<IPosService, PosService>();
		builder.Services.AddScoped<ICobranzaService, CobranzaService>();
		builder.Services.AddScoped<IVentasService, VentasService>();
		builder.Services.AddScoped<IProductosService, ProductosService>();
		builder.Services.AddScoped<IBitacoraService, BitacoraService>();
		builder.Services.AddScoped<IMaquinariaService, MaquinariaService>();

		// ---- Biometria (embebida en proceso, sin sidecar HTTP - ver doc 04 §3.1) ----
		var templatesDir = Path.Combine(dataDir, "Templates");
		builder.Services.AddSingleton<IBiometricCaptureService>(_ => new BiometricCaptureService(templatesDir));

		// ---- Kiosco como ventana MAUI separada (ver docs/migracion-dotnet) ----
		builder.Services.AddSingleton<IKioscoWindowService, KioscoWindowService>();

		// ---- Retroalimentacion sonora del Kiosco ----
		builder.AddAudio();
		builder.Services.AddSingleton<IKioscoSoundService, KioscoSoundService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	/// <summary>
	/// Seed idempotente del catálogo de acciones para SUPERADMIN (authorization).
	/// Solo actúa la primera vez; si permisos_rol ya tiene filas, no hace nada.
	/// </summary>
	private static void SeedPermisosSuperadmin(string dbPath, string dataDir)
	{
		var roles = new RolesRepository(dbPath);
		var permisos = new PermisosRolRepository(dbPath);
		var auth = new AuthService(
			new UsuariosRepository(dbPath),
			new SesionesRepository(dbPath),
			new CuentasRecordadasRepository(dbPath),
			new SessionState(),
			new SesionStore(dataDir));
		var authz = new AuthorizationService(auth, roles, permisos);
		authz.SeedSuperadminPermisosAsync().GetAwaiter().GetResult();
	}
}

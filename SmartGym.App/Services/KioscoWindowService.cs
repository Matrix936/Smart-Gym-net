using Microsoft.Maui.Controls;
using SmartGym.Core.Biometrics;

namespace SmartGym.App.Services;

/// <summary>
/// Ver IKioscoWindowService. La ventana del Kiosco abre con chrome normal (bordes y
/// controles estándar de Windows — a diferencia del prototipo sin-chrome que solo se
/// usó para validar viabilidad, ver docs/migracion-dotnet). Cerrarla (X, Alt+F4, o
/// cualquier vía nativa) se intercepta vía AppWindow.Closing para confirmar antes de
/// soltar el lector — no hay ruta de salida con credenciales, es solo una confirmación.
/// </summary>
public sealed class KioscoWindowService : IKioscoWindowService
{
	private readonly IBiometricCaptureService _captureService;
	private Window? _ventana;

	public KioscoWindowService(IBiometricCaptureService captureService)
	{
		_captureService = captureService;
	}

	public void Abrir()
	{
		if (_ventana is not null)
		{
			TraerAlFrente(_ventana);
			return;
		}

		var page = new KioscoWindowPage();
		var window = new Window(page) { Title = "Smart Gym — Kiosco" };
		window.Created += OnWindowCreated;
		window.Destroying += (_, _) => _ventana = null;

		_ventana = window;
		Application.Current?.OpenWindow(window);
	}

	private void OnWindowCreated(object? sender, EventArgs e)
	{
		if (sender is not Window window)
		{
			return;
		}

#if WINDOWS
		if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
		{
			if (native.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
			{
				presenter.Maximize();
			}

			native.AppWindow.Closing += (_, args) => OnClosing(window, native, args);
		}
#endif
	}

#if WINDOWS
	private void OnClosing(Window window, Microsoft.UI.Xaml.Window native, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
	{
		// AppWindow.Closing debe resolverse de forma síncrona: no se puede "await" la
		// confirmación aquí. Se cancela el cierre inmediato y, si el usuario confirma
		// en el ContentDialog, se cierra la ventana de verdad por código.
		args.Cancel = true;

		_ = ConfirmarYCerrarAsync(window, native);
	}

	private async Task ConfirmarYCerrarAsync(Window window, Microsoft.UI.Xaml.Window native)
	{
		var xamlRoot = native.Content?.XamlRoot;
		if (xamlRoot is null)
		{
			return;
		}

		var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
		{
			XamlRoot = xamlRoot,
			Title = "Detener el modo Kiosco",
			Content = "¿Detener el modo Kiosco?",
			PrimaryButtonText = "Detener",
			CloseButtonText = "Cancelar",
			DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
		};

		var resultado = await dialog.ShowAsync();
		if (resultado != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
		{
			return;
		}

		// Mismo cuidado que KioscoPage.Dispose(): no dejar el lector escuchando sin
		// dueño. Es intencionalmente redundante con ese Dispose() (idempotente) —
		// aquí se hace explícito en vez de confiar en el orden de desmontaje del
		// circuito de Blazor al destruirse el BlazorWebView.
		if (_captureService.CurrentMode == BiometricCaptureMode.Identifying)
		{
			_captureService.StopIdentification();
		}

		Application.Current?.CloseWindow(window);
	}

	private static void TraerAlFrente(Window window)
	{
		if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
		{
			native.Activate();
		}
	}
#else
	private static void TraerAlFrente(Window window)
	{
	}
#endif
}

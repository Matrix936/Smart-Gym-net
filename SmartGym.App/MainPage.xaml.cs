using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace SmartGym.App;

public partial class MainPage : ContentPage
{
	private bool _dragOverHookeado;
	private bool _arrastreArchivoActivo;

	public MainPage()
	{
		InitializeComponent();
	}

	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();

#if WINDOWS
		if (_dragOverHookeado)
		{
			return;
		}
		if (blazorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 wv2)
		{
			_dragOverHookeado = true;
			wv2.DragOver += OnWebViewDragOver;
			wv2.DragLeave += OnWebViewDragLeave;
			wv2.Drop += OnWebViewDrop;

			// Capa 1 anti-bypass del bloqueo por inactividad: WebView2 hereda los
			// aceleradores de Chromium y Ctrl+R/F5/Ctrl+Shift+R recargaban el
			// WebView — AppShell.RestaurarSesionAsync() re-creaba la sesión y el
			// bloqueo (estado en memoria de MainLayout) se evaporaba.
			wv2.CoreWebView2Initialized += (s, e) => ConfigurarSeguridadWebView(s.CoreWebView2);
			if (wv2.CoreWebView2 is not null)
			{
				ConfigurarSeguridadWebView(wv2.CoreWebView2);
			}
		}
#endif
	}

#if WINDOWS
	/// <summary>
	/// Sin aceleradores de navegador en la ventana administrativa: un usuario
	/// nunca debería poder "recargar" la app como página web. DevTools (F12)
	/// solo en builds Debug para poder diagnosticar el DOM.
	/// </summary>
	private static void ConfigurarSeguridadWebView(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2)
	{
		coreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if DEBUG
		coreWebView2.Settings.AreDevToolsEnabled = true;
#else
		coreWebView2.Settings.AreDevToolsEnabled = false;
#endif
	}
#endif

#if WINDOWS
	private void OnWebViewDragOver(object? sender, Microsoft.UI.Xaml.DragEventArgs e)
	{
		if (!e.DataView.Contains(StandardDataFormats.StorageItems))
		{
			return;
		}
		e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
		e.Handled = true;
		if (!_arrastreArchivoActivo)
		{
			_arrastreArchivoActivo = true;
			NotificarDragExterno(true);
		}
	}

	private void OnWebViewDragLeave(object? sender, Microsoft.UI.Xaml.DragEventArgs e)
	{
		if (_arrastreArchivoActivo)
		{
			_arrastreArchivoActivo = false;
			NotificarDragExterno(false);
		}
	}

	private async void OnWebViewDrop(object? sender, Microsoft.UI.Xaml.DragEventArgs e)
	{
		if (!e.DataView.Contains(StandardDataFormats.StorageItems))
		{
			return;
		}
		e.Handled = true;
		if (_arrastreArchivoActivo)
		{
			_arrastreArchivoActivo = false;
			NotificarDragExterno(false);
		}

		try
		{
			var items = await e.DataView.GetStorageItemsAsync();
			if (items.Count == 0 || items[0] is not StorageFile file)
			{
				return;
			}

			var bytes = await File.ReadAllBytesAsync(file.Path);
			if (bytes.Length == 0)
			{
				return;
			}

			var mime = MimePorExtension(file.Name);
			var payload = JsonSerializer.Serialize(new
			{
				dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}",
				mime,
				size = bytes.Length,
				name = file.Name,
			});

			if (blazorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 wv2 &&
				wv2.CoreWebView2 is not null)
			{
				await wv2.CoreWebView2.ExecuteScriptAsync($"window.sgFile && window.sgFile.setExternalDrop({payload});");
			}
		}
		catch
		{
			// El propio SetLogo valida y notifica; aquí solo se evita un crash del arrastre.
		}
	}

	private void NotificarDragExterno(bool activo)
	{
		if (blazorWebView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 wv2 ||
			wv2.CoreWebView2 is null)
		{
			return;
		}
		try
		{
			_ = wv2.CoreWebView2.ExecuteScriptAsync($"window.sgFile && window.sgFile.setExternalDrag({(activo ? "true" : "false")});");
		}
		catch
		{
		}
	}

	private static string MimePorExtension(string nombre)
	{
		var ext = System.IO.Path.GetExtension(nombre).ToLowerInvariant();
		return ext switch
		{
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".svg" => "image/svg+xml",
			_ => "application/octet-stream",
		};
	}
#endif
}
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;

namespace SmartGym.App.Services;

/// <summary>
/// Categorías de resultado del Kiosco con retroalimentación sonora. Es un
/// mapeo intencionalmente grueso: los mp3 disponibles son 3 y cada estado
/// real del Kiosco debe traducirse a UNA de estas categorías en un único
/// punto (ver KioscoPage.MostrarResultado), nunca repartido por el código.
/// </summary>
public enum KioscoResultadoSonido { Concedido, Denegado, NoIdentificado }

public interface IKioscoSoundService
{
	/// <summary>
	/// Fire-and-forget: reproduce el sonido de la categoría sin bloquear al
	/// llamador (el callback del SDK o el dispatcher de Blazor). Errores de
	/// audio se tragan a propósito: el sonido jamás debe romper el flujo de
	/// acceso.
	/// </summary>
	void Reproducir(KioscoResultadoSonido categoria);
}

public class KioscoSoundService : IKioscoSoundService
{
	// Rutas lógicas MauiAsset (Resources/Raw/sounds/, ver SmartGym.App.csproj).
	private static readonly Dictionary<KioscoResultadoSonido, string> Archivos = new()
	{
		[KioscoResultadoSonido.Concedido] = "sounds/Success.mp3",
		[KioscoResultadoSonido.Denegado] = "sounds/Error.mp3",
		[KioscoResultadoSonido.NoIdentificado] = "sounds/Unknown.mp3",
	};

	private readonly IAudioManager _audio;
	private readonly ILogger<KioscoSoundService> _logger;
	private IAudioPlayer? _playerActual;

	public KioscoSoundService(IAudioManager audio, ILogger<KioscoSoundService> logger)
	{
		_audio = audio;
		_logger = logger;
	}

	public void Reproducir(KioscoResultadoSonido categoria)
	{
		_ = ReproducirInternoAsync(categoria);
	}

	private async Task ReproducirInternoAsync(KioscoResultadoSonido categoria)
	{
		try
		{
			var stream = await FileSystem.OpenAppPackageFileAsync(Archivos[categoria]);

			// Un player nuevo por reproducción; si quedó uno sonando (dos
			// identificaciones seguidas y rápidas), se descarta para que el
			// sonido nuevo lo corte limpio en vez de superponerse.
			lock (this)
			{
				_playerActual?.Dispose();
				_playerActual = _audio.CreatePlayer(stream);
				_playerActual.Play();
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "No se pudo reproducir el sonido del Kiosco ({Categoria})", categoria);
		}
	}
}

namespace SmartGym.App.Services;

/// <summary>
/// Notificación de cambios de logo de la empresa: mismo patrón que ThemeState.
/// ILogoStorage lee del disco al montar (Login se remonta siempre, así que le
/// basta releer), pero el sidebar persiste entre navegaciones y necesita el
/// evento para reflejarse sin recargar la app cuando Configuración lo edita.
/// Singleton: los componentes viven en scopes distintos.
/// </summary>
public interface ILogoState
{
    event Action? Changed;
    void NotificarCambio();
}

public sealed class LogoState : ILogoState
{
    public event Action? Changed;

    public void NotificarCambio() => Changed?.Invoke();
}

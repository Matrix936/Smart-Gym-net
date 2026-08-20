namespace SmartGym.App.Services;

/// <summary>
/// Estado de tema compartido: sincroniza el toggle custom (sgTheme, CSS vars de app.css)
/// con el MudThemeProvider de MudBlazor. Una sola fuente de verdad por sesión.
/// </summary>
public interface IThemeState
{
    bool IsDark { get; }
    event Action? Changed;
    void SetDark(bool isDark);
}

public sealed class ThemeState : IThemeState
{
    public bool IsDark { get; private set; } = true;

    public event Action? Changed;

    public void SetDark(bool isDark)
    {
        if (IsDark == isDark)
        {
            return;
        }

        IsDark = isDark;
        Changed?.Invoke();
    }
}

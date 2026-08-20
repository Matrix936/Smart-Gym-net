namespace SmartGym.App.Services;

public enum FeedbackSeverity
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>Mensaje global (equivalente a FeedbackContext + FeedbackSnackbar de React).</summary>
public sealed class FeedbackMessage
{
    public required string Text { get; init; }
    public required FeedbackSeverity Severity { get; init; }
    public int DurationMs { get; init; }
}

/// <summary>
/// Canal global de notificaciones: una sola instancia de snackbar en el árbol
/// (FeedbackSnackbar) se suscribe a <see cref="Changed"/>. Nunca usar alertas
/// locales ad-hoc (05-convenciones-ui-ux.md §10).
/// </summary>
public interface IFeedbackService
{
    event Action<FeedbackMessage>? Changed;
    void Show(string message, FeedbackSeverity severity);
    void ShowSuccess(string message);
    void ShowInfo(string message);
    void ShowWarning(string message);
    void ShowError(string message);
}

public sealed class FeedbackService : IFeedbackService
{
    private static readonly Dictionary<FeedbackSeverity, int> Duraciones = new()
    {
        [FeedbackSeverity.Success] = 3500,
        [FeedbackSeverity.Info] = 3500,
        [FeedbackSeverity.Warning] = 4000,
        [FeedbackSeverity.Error] = 5000,
    };

    public event Action<FeedbackMessage>? Changed;

    public void Show(string message, FeedbackSeverity severity)
        => Changed?.Invoke(new FeedbackMessage
        {
            Text = message,
            Severity = severity,
            DurationMs = Duraciones[severity],
        });

    public void ShowSuccess(string message) => Show(message, FeedbackSeverity.Success);
    public void ShowInfo(string message) => Show(message, FeedbackSeverity.Info);
    public void ShowWarning(string message) => Show(message, FeedbackSeverity.Warning);
    public void ShowError(string message) => Show(message, FeedbackSeverity.Error);
}

namespace SmartGym.Core.Biometrics;

/// <summary>
/// Contrato de dominio para la captura biométrica embebida en el proceso (sin
/// sidecar HTTP separado — ver prototipo validado con hardware real en
/// docs/migracion-dotnet/04-integracion-biometrica.md §3.1). La implementación
/// concreta vive en SmartGym.App porque depende del SDK DigitalPersona y de
/// que exista un hilo/mensaje pump adecuado (MAUI/WinUI3), no de infraestructura
/// de datos.
/// </summary>
public interface IBiometricCaptureService
{
    /// <summary>
    /// Estado en memoria del servicio, para que el arbitraje ("esperando cede,
    /// en curso no se interrumpe") se pueda consultar desde fuera antes de
    /// iniciar una nueva sesión.
    /// </summary>
    BiometricCaptureMode CurrentMode { get; }

    bool IsReaderConnected { get; }

    /// <summary>
    /// Inicia una sesión de enrolamiento y espera hasta que termine (completada,
    /// con error o cancelada). Reportado con default en 4 muestras - el mismo
    /// número que usaba BiometricEngine.cs del prototipo Tauri (ver doc 04 §4:
    /// "enrollment completo en 4 toques").
    /// Si templatesExistentes no es null, verifica el FeatureSet final contra
    /// cada template antes de guardarlo. Si hay match, retorna Estado=Duplicado.
    /// </summary>
    Task<EnrollmentResult> StartEnrollmentAsync(string idSocio, string dedo, int muestrasRequeridas = 4,
        IReadOnlyList<string>? templatesExistentes = null, CancellationToken ct = default);

    /// <summary>Cancela una sesión de enrolamiento en curso. No-op si CurrentMode no es Enrolling.</summary>
    void CancelEnrollment();

    /// <summary>
    /// Pone el servicio en modo identificación continua (Kiosco): cada dedo que
    /// toque el sensor dispara un intento de match 1:N contra templatePaths y
    /// un evento IdentificationResultReceived, hasta llamar StopIdentification().
    /// </summary>
    Task StartIdentificationAsync(IReadOnlyList<string> templatePaths, CancellationToken ct = default);

    /// <summary>Detiene el modo identificación. No-op si CurrentMode no es Identifying.</summary>
    void StopIdentification();

    event EventHandler<EnrollmentStatusEventArgs> EnrollmentStatusChanged;
    event EventHandler<IdentificationResultEventArgs> IdentificationResultReceived;
}

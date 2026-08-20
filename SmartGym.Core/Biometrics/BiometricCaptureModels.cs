namespace SmartGym.Core.Biometrics;

/// <summary>
/// Estado en memoria del servicio de captura — equivalente a ModoActual del
/// prototipo Tauri (BiometricEngine.cs), pero expuesto como propiedad de
/// solo lectura del contrato: es donde el arbitraje ("esperando cede, en
/// curso no se interrumpe") se consulta desde fuera del servicio.
/// </summary>
public enum BiometricCaptureMode
{
    Idle,
    Enrolling,
    Identifying,
}

/// <summary>Etapa puntual de una sesión de enrolamiento en curso o finalizada.</summary>
public enum EnrollmentStage
{
    EsperandoDedo,
    Capturando,
    Completado,
    Error,
    Cancelado,
}

/// <summary>Resultado final de <see cref="IBiometricCaptureService.StartEnrollmentAsync"/>.</summary>
public sealed class EnrollmentResult
{
    public required EnrollmentStage Estado { get; init; }
    public string? TemplatePath { get; init; }
    public string? Error { get; init; }
    public int MuestrasCapturadas { get; init; }
    public int MuestrasRequeridas { get; init; }
}

/// <summary>
/// Progreso de una sesión de enrolamiento en curso (EnrollmentStatusChanged) —
/// incluye MuestrasCapturadas/MuestrasRequeridas para que la UI muestre avance
/// ("3 de 4"), no solo el resultado final.
/// </summary>
public sealed class EnrollmentStatusEventArgs : EventArgs
{
    public required EnrollmentStage Estado { get; init; }
    public int MuestrasCapturadas { get; init; }
    public int MuestrasRequeridas { get; init; }
    public string? TemplatePath { get; init; }
    public string? Error { get; init; }
}

/// <summary>Resultado de un intento de identificación 1:N (Kiosco).</summary>
public sealed class IdentificationResultEventArgs : EventArgs
{
    public required bool Identificado { get; init; }
    public string? TemplatePath { get; init; }
    public string? IdSocio { get; init; }
}

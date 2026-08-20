namespace SmartGym.Core.Biometrics;

/// <summary>
/// Maquina de estados pura del arbitraje de modo — sin dependencia del SDK.
/// Extraida para poder testear las transiciones sin hardware (SmartGym.Tests
/// no puede referenciar SmartGym.App: distinto TFM, solo-Windows/MAUI).
///
/// Criterio "esperando vs en curso" heredado del BiometricEngine.cs original
/// de Tauri (ver docs/migracion-dotnet/04-integracion-biometrica.md): una
/// identificacion que esta esperando dedo (nadie tocando el sensor) SI se
/// puede interrumpir para dar paso a un enrolamiento; una identificacion con
/// el dedo puesto (procesando una muestra) o un enrolamiento en curso NUNCA
/// se interrumpen.
/// </summary>
public sealed class BiometricCaptureArbiter
{
    private readonly object _lock = new();
    private BiometricCaptureMode _mode = BiometricCaptureMode.Idle;
    private bool _identificacionEsperando = true;

    public BiometricCaptureMode CurrentMode { get { lock (_lock) return _mode; } }

    /// <summary>
    /// Intenta pasar a Enrolling. Devuelve false si el lector esta ocupado con
    /// algo que no se puede interrumpir (enrolamiento en curso, o
    /// identificacion con el dedo puesto). interrumpioIdentificacion indica si
    /// se corto una identificacion que estaba esperando dedo.
    /// </summary>
    public bool TryStartEnrollment(out bool interrumpioIdentificacion)
    {
        lock (_lock)
        {
            interrumpioIdentificacion = false;

            if (_mode == BiometricCaptureMode.Enrolling)
            {
                return false;
            }

            if (_mode == BiometricCaptureMode.Identifying)
            {
                if (!_identificacionEsperando)
                {
                    return false;
                }

                interrumpioIdentificacion = true;
            }

            _mode = BiometricCaptureMode.Enrolling;
            return true;
        }
    }

    /// <summary>Devuelve false si el lector esta ocupado (nunca se interrumpe nada para iniciar identificacion).</summary>
    public bool TryStartIdentification()
    {
        lock (_lock)
        {
            if (_mode != BiometricCaptureMode.Idle)
            {
                return false;
            }

            _mode = BiometricCaptureMode.Identifying;
            _identificacionEsperando = true;
            return true;
        }
    }

    public void StopIdentification()
    {
        lock (_lock)
        {
            if (_mode != BiometricCaptureMode.Identifying)
            {
                return;
            }

            _mode = BiometricCaptureMode.Idle;
        }
    }

    /// <summary>Vuelve a Idle si estaba Enrolling (completado, error o cancelado). No-op en cualquier otro modo.</summary>
    public void FinishEnrollment()
    {
        lock (_lock)
        {
            if (_mode == BiometricCaptureMode.Enrolling)
            {
                _mode = BiometricCaptureMode.Idle;
            }
        }
    }

    /// <summary>Marca "en curso" si el modo actual es Identifying; no-op en cualquier otro modo.</summary>
    public void OnFingerTouch()
    {
        lock (_lock)
        {
            if (_mode == BiometricCaptureMode.Identifying)
            {
                _identificacionEsperando = false;
            }
        }
    }

    /// <summary>Marca "esperando" si el modo actual es Identifying; no-op en cualquier otro modo.</summary>
    public void OnFingerGone()
    {
        lock (_lock)
        {
            if (_mode == BiometricCaptureMode.Identifying)
            {
                _identificacionEsperando = true;
            }
        }
    }
}

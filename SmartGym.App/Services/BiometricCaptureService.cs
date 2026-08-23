using DPFP.Capture;
using DPFP.Processing;
using DPFP.Verification;
using SmartGym.Core.Biometrics;

namespace SmartGym.App.Services;

/// <summary>
/// Implementacion real de IBiometricCaptureService. Evoluciona
/// BiometricPrototype/CapturePrototypeService.cs (validado con hardware real,
/// lector U.are.U 4500, dos corridas independientes sin excepciones — ver
/// docs/migracion-dotnet/04-integracion-biometrica.md §3.1): mismo
/// DPFP.Capture.EventHandler embebido sin Form, mismo StartCapture() en
/// construccion. El arbitraje de modo (incluido el criterio "esperando vs en
/// curso") vive en BiometricCaptureArbiter (SmartGym.Core), separado del SDK
/// para poder testearse sin hardware. Esta clase se ocupa de lo que si
/// depende del SDK: extraccion de features con DataPurpose dinamico segun el
/// modo (bug documentado en doc 04 §4), guardado de templates y matching 1:N.
/// </summary>
public sealed class BiometricCaptureService : DPFP.Capture.EventHandler, IBiometricCaptureService, IDisposable
{
    private readonly DPFP.Capture.Capture _capturer = new();
    private readonly Enrollment _enroller = new();
    private readonly Verification _verificator = new();
    private readonly BiometricCaptureArbiter _arbiter = new();
    private readonly object _lock = new();
    private readonly string _templatesDir;
    private readonly string _logPath;
    private readonly object _fileLock = new();
    private bool _disposed;

    private bool _readerConnected;

    // Sesion de enrolamiento en curso. Solo se leen/escriben dentro de _lock.
    private TaskCompletionSource<EnrollmentResult>? _enrollmentTcs;
    private CancellationTokenRegistration _enrollmentCtReg;
    private string _enrollIdSocio = string.Empty;
    private string _enrollDedo = string.Empty;
    private int _muestrasRequeridas;
    private int _muestrasCapturadas;

    // Sesion de identificacion en curso. SocioId se deriva del nombre de
    // archivo (prefijo antes del primer '_') por convencion de
    // GuardarTemplate — acoplamiento documentado, no accidental.
    private List<TemplateCargado>? _templatesCargados;
    private List<TemplateCargado>? _templatesExistentesDuplicado;

    public BiometricCaptureMode CurrentMode => _arbiter.CurrentMode;
    public bool IsReaderConnected { get { lock (_lock) return _readerConnected; } }

    public event EventHandler<EnrollmentStatusEventArgs>? EnrollmentStatusChanged;
    public event EventHandler<IdentificationResultEventArgs>? IdentificationResultReceived;

    public BiometricCaptureService(string templatesDir)
    {
        _templatesDir = templatesDir;
        Directory.CreateDirectory(_templatesDir);

        _logPath = Path.Combine(AppContext.BaseDirectory, "BiometricCapture", "capture_log.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);

        _capturer.EventHandler = this;
        _capturer.StartCapture();
    }

    public Task<EnrollmentResult> StartEnrollmentAsync(string idSocio, string dedo, int muestrasRequeridas = 4,
        IReadOnlyList<string>? templatesExistentes = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idSocio))
        {
            throw new ArgumentException("idSocio es obligatorio", nameof(idSocio));
        }

        if (string.IsNullOrWhiteSpace(dedo))
        {
            throw new ArgumentException("dedo es obligatorio", nameof(dedo));
        }

        if (!_arbiter.TryStartEnrollment(out var interrumpioIdentificacion))
        {
            throw new InvalidOperationException($"Lector ocupado (modo actual: {CurrentMode}); no se interrumpe una captura en curso.");
        }

        TaskCompletionSource<EnrollmentResult> tcs;

        lock (_lock)
        {
            if (interrumpioIdentificacion)
            {
                _templatesCargados = null;
                Log("StartEnrollmentAsync interrumpe identificacion en espera (sin dedo detectado)");
            }

            _enrollIdSocio = idSocio;
            _enrollDedo = dedo;
            _muestrasRequeridas = muestrasRequeridas;
            _muestrasCapturadas = 0;
            _enroller.Clear();
            _templatesExistentesDuplicado = templatesExistentes is not null && templatesExistentes.Count > 0
                ? CargarTemplates(templatesExistentes)
                : null;

            tcs = new TaskCompletionSource<EnrollmentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _enrollmentTcs = tcs;
        }

        RaiseEnrollmentStatus(EnrollmentStage.EsperandoDedo, muestrasCapturadas: 0, muestrasRequeridas);

        _enrollmentCtReg = ct.Register(CancelEnrollment);

        return tcs.Task;
    }

    public void CancelEnrollment()
    {
        if (CurrentMode != BiometricCaptureMode.Enrolling)
        {
            return;
        }

        TaskCompletionSource<EnrollmentResult>? tcs;
        int muestrasCapturadas;
        int muestrasRequeridas;

        lock (_lock)
        {
            tcs = _enrollmentTcs;
            muestrasCapturadas = _muestrasCapturadas;
            muestrasRequeridas = _muestrasRequeridas;
            FinalizarEnrollmentSinLock();
        }

        _arbiter.FinishEnrollment();
        Log("CancelEnrollment");

        var resultado = new EnrollmentResult
        {
            Estado = EnrollmentStage.Cancelado,
            MuestrasCapturadas = muestrasCapturadas,
            MuestrasRequeridas = muestrasRequeridas,
        };
        tcs?.TrySetResult(resultado);
        RaiseEnrollmentStatus(EnrollmentStage.Cancelado, muestrasCapturadas, muestrasRequeridas);
    }

    public Task StartIdentificationAsync(IReadOnlyList<string> templatePaths, CancellationToken ct = default)
    {
        if (templatePaths is null || templatePaths.Count == 0)
        {
            throw new ArgumentException("templatePaths es obligatorio y no puede estar vacio", nameof(templatePaths));
        }

        if (!_arbiter.TryStartIdentification())
        {
            throw new InvalidOperationException($"Lector ocupado (modo actual: {CurrentMode}); no se interrumpe una sesion en curso.");
        }

        lock (_lock)
        {
            _templatesCargados = CargarTemplates(templatePaths);
        }

        Log($"StartIdentificationAsync - {templatePaths.Count} templates");
        return Task.CompletedTask;
    }

    public void StopIdentification()
    {
        if (CurrentMode != BiometricCaptureMode.Identifying)
        {
            return;
        }

        _arbiter.StopIdentification();
        lock (_lock) { _templatesCargados = null; }

        Log("StopIdentification");
    }

    // ── DPFP.Capture.EventHandler ────────────────────────────────────────────

    public void OnReaderConnect(object capture, string readerSerial)
    {
        lock (_lock) { _readerConnected = true; }
        Log($"OnReaderConnect - Serial: {readerSerial}");
    }

    public void OnReaderDisconnect(object capture, string readerSerial)
    {
        lock (_lock) { _readerConnected = false; }
        Log($"OnReaderDisconnect - Serial: {readerSerial}");
    }

    public void OnFingerTouch(object capture, string readerSerial)
    {
        _arbiter.OnFingerTouch();
        Log($"OnFingerTouch - Serial: {readerSerial}");
    }

    public void OnFingerGone(object capture, string readerSerial)
    {
        _arbiter.OnFingerGone();
        Log($"OnFingerGone - Serial: {readerSerial}");
    }

    public void OnSampleQuality(object capture, string readerSerial, CaptureFeedback feedback) =>
        Log($"OnSampleQuality - Feedback: {feedback}");

    public void OnComplete(object capture, string readerSerial, DPFP.Sample sample)
    {
        Log($"OnComplete - Serial: {readerSerial} Sample: {sample is not null}");

        if (sample is null)
        {
            return;
        }

        var modo = CurrentMode;

        try
        {
            // DataPurpose dinamico segun el modo actual — bug documentado en
            // doc 04 §4 (el enrollment fallaba al 4o toque cuando se
            // hardcodeaba Verification).
            var purpose = modo == BiometricCaptureMode.Enrolling
                ? DataPurpose.Enrollment
                : DataPurpose.Verification;

            var features = ExtractFeatures(sample, purpose);
            if (features is null)
            {
                return;
            }

            if (modo == BiometricCaptureMode.Enrolling)
            {
                // Doble extracción de FeatureSet: el enrolador necesita
                // DataPurpose.Enrollment y la detección de duplicados necesita
                // DataPurpose.Verification — son formatos internos diferentes
                // del SDK DPFP (mismo patrón de bug que doc 04 §4).
                var verifFeatures = ExtractFeatures(sample, DataPurpose.Verification);
                HandleEnrollmentSample(features, verifFeatures);
            }
            else if (modo == BiometricCaptureMode.Identifying)
            {
                HandleIdentificationSample(features);
            }
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION OnComplete: {ex}");
            if (modo == BiometricCaptureMode.Enrolling)
            {
                FallarEnrollment(ex.Message);
            }
        }
    }

    // ── Enrolamiento ──────────────────────────────────────────────────────────

    private void HandleEnrollmentSample(DPFP.FeatureSet features, DPFP.FeatureSet? verifFeatures)
    {
        TaskCompletionSource<EnrollmentResult>? tcs = null;
        EnrollmentResult? resultado = null;
        var etapaProgreso = EnrollmentStage.Capturando;
        int muestrasCapturadas;
        int muestrasRequeridas;
        var completoEnrollment = false;

        lock (_lock)
        {
            if (CurrentMode != BiometricCaptureMode.Enrolling)
            {
                return;
            }

            // El SDK DPFP puede lanzar en AddFeatures cuando detecta duplicado
            // internamente (HRESULT 0xFFFFFFE3) ANTES de que TemplateStatus
            // llegue a Ready — capturamos la excepción y la traducimos al
            // estado Duplicado para que la UI muestre el mensaje amigable.
            _enroller.AddFeatures(features);

            if (!completoEnrollment)
            {
                switch (_enroller.TemplateStatus)
                {
                    case Enrollment.Status.Insufficient:
                        _muestrasCapturadas++;
                        break;

                case Enrollment.Status.Ready:
                    // Detección de duplicados: usa el FeatureSet extraído con
                    // DataPurpose.Verification (NO el de Enrollment) porque
                    // el motor de verificación DPFP lo requiere — mismo bug
                    // del DataPurpose documentado en doc 04 §4.
                    if (_templatesExistentesDuplicado is not null && verifFeatures is not null)
                    {
                        foreach (var entry in _templatesExistentesDuplicado)
                        {
                            var verif = new Verification.Result();
                            _verificator.Verify(verifFeatures, entry.Template, ref verif);
                            if (verif.Verified)
                            {
                                _muestrasCapturadas = _muestrasRequeridas;
                                tcs = _enrollmentTcs;
                                resultado = new EnrollmentResult
                                {
                                    Estado = EnrollmentStage.Duplicado,
                                    MuestrasCapturadas = _muestrasCapturadas,
                                    MuestrasRequeridas = _muestrasRequeridas,
                                };
                                etapaProgreso = EnrollmentStage.Duplicado;
                                completoEnrollment = true;
                                FinalizarEnrollmentSinLock();
                                break;
                            }
                        }

                        if (completoEnrollment)
                        {
                            break;
                        }
                    }

                        var path = GuardarTemplate(_enroller.Template);
                        _muestrasCapturadas = _muestrasRequeridas;
                        tcs = _enrollmentTcs;
                        resultado = new EnrollmentResult
                        {
                            Estado = EnrollmentStage.Completado,
                            TemplatePath = path,
                            MuestrasCapturadas = _muestrasCapturadas,
                            MuestrasRequeridas = _muestrasRequeridas,
                        };
                        etapaProgreso = EnrollmentStage.Completado;
                        completoEnrollment = true;
                        FinalizarEnrollmentSinLock();
                        break;

                    case Enrollment.Status.Failed:
                        tcs = _enrollmentTcs;
                        resultado = new EnrollmentResult
                        {
                            Estado = EnrollmentStage.Error,
                            Error = "Fallo al procesar huella",
                            MuestrasCapturadas = _muestrasCapturadas,
                            MuestrasRequeridas = _muestrasRequeridas,
                        };
                        etapaProgreso = EnrollmentStage.Error;
                        completoEnrollment = true;
                        FinalizarEnrollmentSinLock();
                        break;
                }
            }
            else
            {
                _arbiter.FinishEnrollment();
            }

            muestrasCapturadas = _muestrasCapturadas;
            muestrasRequeridas = _muestrasRequeridas;
        }

        if (completoEnrollment)
        {
            _arbiter.FinishEnrollment();
        }

        RaiseEnrollmentStatus(etapaProgreso, muestrasCapturadas, muestrasRequeridas, resultado?.TemplatePath, resultado?.Error);
        if (resultado is not null)
        {
            tcs?.TrySetResult(resultado);
        }
    }

    private void FallarEnrollment(string error)
    {
        TaskCompletionSource<EnrollmentResult>? tcs;
        EnrollmentResult resultado;
        int muestrasCapturadas;
        int muestrasRequeridas;

        lock (_lock)
        {
            if (CurrentMode != BiometricCaptureMode.Enrolling)
            {
                return;
            }

            tcs = _enrollmentTcs;
            muestrasCapturadas = _muestrasCapturadas;
            muestrasRequeridas = _muestrasRequeridas;
            resultado = new EnrollmentResult
            {
                Estado = EnrollmentStage.Error,
                Error = error,
                MuestrasCapturadas = muestrasCapturadas,
                MuestrasRequeridas = muestrasRequeridas,
            };
            FinalizarEnrollmentSinLock();
        }

        _arbiter.FinishEnrollment();
        RaiseEnrollmentStatus(EnrollmentStage.Error, muestrasCapturadas, muestrasRequeridas, error: error);
        tcs?.TrySetResult(resultado);
    }

    /// <summary>Debe llamarse dentro de lock(_lock). Limpia el estado de la sesion (el modo lo maneja el arbiter aparte).</summary>
    private void FinalizarEnrollmentSinLock()
    {
        _enrollmentTcs = null;
        _enrollmentCtReg.Dispose();
        _enrollmentCtReg = default;
    }

    private string GuardarTemplate(DPFP.Template template)
    {
        var fileName = $"{_enrollIdSocio}_{_enrollDedo}_{Guid.NewGuid()}.bin";
        var path = Path.Combine(_templatesDir, fileName);
        using var stream = File.Create(path);
        template.Serialize(stream);
        return path;
    }

    private void RaiseEnrollmentStatus(EnrollmentStage estado, int muestrasCapturadas, int muestrasRequeridas, string? templatePath = null, string? error = null)
    {
        EnrollmentStatusChanged?.Invoke(this, new EnrollmentStatusEventArgs
        {
            Estado = estado,
            MuestrasCapturadas = muestrasCapturadas,
            MuestrasRequeridas = muestrasRequeridas,
            TemplatePath = templatePath,
            Error = error,
        });
    }

    // ── Identificacion ───────────────────────────────────────────────────────

    private void HandleIdentificationSample(DPFP.FeatureSet features)
    {
        List<TemplateCargado>? templates;
        lock (_lock)
        {
            if (CurrentMode != BiometricCaptureMode.Identifying)
            {
                return;
            }

            templates = _templatesCargados;
        }

        if (templates is null || templates.Count == 0)
        {
            IdentificationResultReceived?.Invoke(this, new IdentificationResultEventArgs { Identificado = false });
            return;
        }

        foreach (var entry in templates)
        {
            var resultado = new Verification.Result();
            _verificator.Verify(features, entry.Template, ref resultado);
            if (resultado.Verified)
            {
                IdentificationResultReceived?.Invoke(this, new IdentificationResultEventArgs
                {
                    Identificado = true,
                    TemplatePath = entry.Path,
                    IdSocio = entry.SocioId,
                });
                return;
            }
        }

        IdentificationResultReceived?.Invoke(this, new IdentificationResultEventArgs { Identificado = false });
    }

    private static List<TemplateCargado> CargarTemplates(IReadOnlyList<string> paths)
    {
        var resultado = new List<TemplateCargado>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            using var stream = File.OpenRead(path);
            var template = new DPFP.Template(stream);
            var fileName = Path.GetFileNameWithoutExtension(path);
            var socioId = fileName.Split('_') is [var primero, ..] ? primero : "unknown";
            resultado.Add(new TemplateCargado(socioId, path, template));
        }

        return resultado;
    }

    private static DPFP.FeatureSet? ExtractFeatures(DPFP.Sample sample, DataPurpose purpose)
    {
        var extractor = new FeatureExtraction();
        var feedback = CaptureFeedback.None;
        var features = new DPFP.FeatureSet();
        extractor.CreateFeatureSet(sample, purpose, ref feedback, ref features);
        return feedback == CaptureFeedback.Good ? features : null;
    }

    private void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        lock (_fileLock)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try { _capturer.StopCapture(); } catch { /* best-effort en shutdown */ }
        _capturer.Dispose();
        _enrollmentCtReg.Dispose();
    }

    private sealed record TemplateCargado(string SocioId, string Path, DPFP.Template Template);
}

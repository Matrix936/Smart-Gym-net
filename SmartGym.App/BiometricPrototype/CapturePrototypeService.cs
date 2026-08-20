using DPFP.Capture;

namespace SmartGym.App.BiometricPrototype;

/// <summary>
/// Prototipo aislado: DPFP.Capture.EventHandler como servicio simple (sin Form),
/// para confirmar si el message pump COM que requiere el SDK existe dentro del
/// proceso MAUI/WinUI3 + WebView2, sin necesidad de una ventana WinForms secundaria.
/// Ver docs/migracion-dotnet/04-integracion-biometrica.md §1 y §6.
/// No se integra a producción: es exclusivamente para /biometric-test.
/// </summary>
public sealed class CapturePrototypeService : DPFP.Capture.EventHandler, IDisposable
{
    private readonly DPFP.Capture.Capture _capturer = new();
    private readonly string _logPath;
    private readonly object _fileLock = new();
    private bool _disposed;

    public event Action<string>? LogLine;

    public CapturePrototypeService()
    {
        _logPath = Path.Combine(AppContext.BaseDirectory, "BiometricPrototype", "proto_log.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        _capturer.EventHandler = this;
    }

    public void StartCapture()
    {
        Log("StartCapture() llamado");
        try
        {
            _capturer.StartCapture();
            Log("StartCapture() OK");
        }
        catch (Exception ex)
        {
            Log("EXCEPTION StartCapture: " + ex);
        }
    }

    public void StopCapture()
    {
        try
        {
            _capturer.StopCapture();
            Log("StopCapture() OK");
        }
        catch (Exception ex)
        {
            Log("EXCEPTION StopCapture: " + ex.Message);
        }
    }

    private void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        lock (_fileLock)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        LogLine?.Invoke(line);
    }

    public void OnReaderConnect(object capture, string readerSerial) =>
        Log($"OnReaderConnect - Serial: {readerSerial}");

    public void OnReaderDisconnect(object capture, string readerSerial) =>
        Log($"OnReaderDisconnect - Serial: {readerSerial}");

    public void OnFingerTouch(object capture, string readerSerial) =>
        Log($"OnFingerTouch - Serial: {readerSerial}");

    public void OnFingerGone(object capture, string readerSerial) =>
        Log($"OnFingerGone - Serial: {readerSerial}");

    public void OnSampleQuality(object capture, string readerSerial, CaptureFeedback feedback) =>
        Log($"OnSampleQuality - Feedback: {feedback}");

    public void OnComplete(object capture, string readerSerial, DPFP.Sample sample) =>
        Log($"OnComplete - Serial: {readerSerial} Sample: {(sample != null)}");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCapture();
        _capturer.Dispose();
    }
}

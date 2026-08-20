using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace SmartGym.Biometrics
{
    public enum ModoActual { Ninguno, Enrolamiento, Identificacion }

    public class BiometricEngine : IDisposable
    {
        private readonly Form _uiForm;
        private readonly object _stateLock = new object();
        private ModoActual _modo = ModoActual.Ninguno;
        private bool _fingerActive = false;

        private bool _readerConnected = false;
        private string _readerSerial = null;

        private string _enrollEstado = "idle";
        private string _enrollIdSocio = null;
        private string _enrollDedo = null;
        private string _enrollTemplatePath = null;
        private string _enrollError = null;
        private int _enrollFeaturesNeeded = 0;

        private string _identifyEstado = "idle";
        private string _identifySocioId = null;
        private string _identifyTemplatePath = null;
        private string _identifyError = null;
        private string[] _currentTemplatePaths = null;
        private List<LoadedTemplate> _loadedTemplates = null;

        private System.Threading.Timer _enrollTimer;
        private bool _disposed = false;

        public bool ReaderConnected { get { lock (_stateLock) return _readerConnected; } }
        public string ReaderSerial { get { lock (_stateLock) return _readerSerial; } }
        public ModoActual Modo { get { lock (_stateLock) return _modo; } }

        public BiometricEngine(Form uiForm)
        {
            _uiForm = uiForm;
        }

        public void OnReaderConnect(string readerSerial)
        {
            lock (_stateLock)
            {
                _readerConnected = true;
                _readerSerial = readerSerial;
            }
        }

        public void OnReaderDisconnect(string readerSerial)
        {
            lock (_stateLock)
            {
                _readerConnected = false;
                _readerSerial = null;
            }
        }

        public void OnFingerTouch()
        {
            lock (_stateLock) { _fingerActive = true; }
        }

        public void OnFingerGone()
        {
            lock (_stateLock) { _fingerActive = false; }
        }

        public (bool ok, string error) TrySetModoEnrolamiento(string idSocio, string dedo)
        {
            if (string.IsNullOrWhiteSpace(idSocio))
                return (false, "id_socio es obligatorio");
            if (string.IsNullOrWhiteSpace(dedo))
                return (false, "dedo es obligatorio");

            lock (_stateLock)
            {
                if (_fingerActive)
                    return (false, "Ocupado: dedo detectado, esperando a que se retire");

                _modo = ModoActual.Enrolamiento;
                _enrollIdSocio = idSocio;
                _enrollDedo = dedo;
                _enrollEstado = "iniciado";
                _enrollTemplatePath = null;
                _enrollError = null;
                _enrollFeaturesNeeded = 0;
            }

            ((CaptureForm)_uiForm).ClearEnrollmentOnUIThread();

            if (_uiForm.IsHandleCreated)
                _uiForm.BeginInvoke(new Action(() => _uiForm.Activate()));

            _enrollTimer?.Dispose();
            _enrollTimer = new System.Threading.Timer((_) =>
            {
                lock (_stateLock)
                {
                    if (_modo == ModoActual.Enrolamiento)
                    {
                        _modo = ModoActual.Ninguno;
                        _enrollEstado = "error";
                        _enrollError = "Tiempo de espera agotado";
                    }
                }
            }, null, Config.EnrollmentTimeoutMs, System.Threading.Timeout.Infinite);

            return (true, null);
        }

        public (bool ok, string error) TrySetModoIdentificacion(List<string> templatePaths)
        {
            if (templatePaths == null || templatePaths.Count == 0)
                return (false, "template_paths es obligatorio y no puede estar vacio");

            lock (_stateLock)
            {
                if (_fingerActive)
                    return (false, "Ocupado: dedo detectado, esperando a que se retire");

                _modo = ModoActual.Identificacion;
                _currentTemplatePaths = templatePaths.ToArray();
                _loadedTemplates = null;
                _identifyEstado = "iniciado";
                _identifySocioId = null;
                _identifyTemplatePath = null;
                _identifyError = null;
            }

            if (_uiForm.IsHandleCreated)
                _uiForm.BeginInvoke(new Action(() => _uiForm.Activate()));

            return (true, null);
        }

        public void CancelarEnrolamiento()
        {
            _enrollTimer?.Dispose();
            lock (_stateLock)
            {
                _modo = ModoActual.Ninguno;
                _enrollEstado = "idle";
                _enrollError = null;
                _enrollTemplatePath = null;
                _enrollIdSocio = null;
                _enrollDedo = null;
            }
        }

        public void CancelarIdentificacion()
        {
            lock (_stateLock)
            {
                _modo = ModoActual.Ninguno;
                _identifyEstado = "idle";
                _identifyError = null;
                _identifySocioId = null;
                _identifyTemplatePath = null;
                _currentTemplatePaths = null;
                _loadedTemplates = null;
            }
        }

        public EnrollStatusResponse GetEnrollStatus()
        {
            lock (_stateLock)
            {
                return new EnrollStatusResponse
                {
                    estado = _enrollEstado,
                    template_path = _enrollTemplatePath,
                    error = _enrollError,
                    features_needed = _enrollFeaturesNeeded
                };
            }
        }

        public IdentifyStatusResponse GetIdentifyStatus()
        {
            lock (_stateLock)
            {
                return new IdentifyStatusResponse
                {
                    estado = _identifyEstado,
                    socio_id = _identifySocioId,
                    template_path = _identifyTemplatePath,
                    error = _identifyError
                };
            }
        }

        public void SetError(string error)
        {
            lock (_stateLock)
            {
                if (_modo == ModoActual.Enrolamiento)
                {
                    _enrollEstado = "error";
                    _enrollError = error;
                    _modo = ModoActual.Ninguno;
                }
                else if (_modo == ModoActual.Identificacion)
                {
                    _identifyEstado = "error";
                    _identifyError = error;
                    _modo = ModoActual.Ninguno;
                }
            }
        }

        public void HandleEnrollmentComplete(DPFP.FeatureSet featureSet, DPFP.Processing.Enrollment enroller)
        {
            lock (_stateLock)
            {
                if (_modo != ModoActual.Enrolamiento) return;
            }

            enroller.AddFeatures(featureSet);

            lock (_stateLock)
            {
                switch (enroller.TemplateStatus)
                {
                    case DPFP.Processing.Enrollment.Status.Insufficient:
                        _enrollFeaturesNeeded = (int)enroller.FeaturesNeeded;
                        _enrollEstado = "esperando_dedo";
                        break;

                    case DPFP.Processing.Enrollment.Status.Ready:
                        var path = SaveTemplate(enroller.Template);
                        _enrollEstado = "completado";
                        _enrollTemplatePath = path;
                        _enrollTimer?.Dispose();
                        _modo = ModoActual.Ninguno;
                        break;

                    case DPFP.Processing.Enrollment.Status.Failed:
                        _enrollEstado = "error";
                        _enrollError = "Fallo al procesar huella";
                        _enrollTimer?.Dispose();
                        _modo = ModoActual.Ninguno;
                        break;
                }
            }
        }

        public void HandleIdentificationComplete(DPFP.FeatureSet featureSet, DPFP.Verification.Verification verificator)
        {
            lock (_stateLock)
            {
                if (_modo != ModoActual.Identificacion) return;
            }

            lock (_stateLock)
            {
                if (_loadedTemplates == null)
                    LoadTemplates(_currentTemplatePaths);

                if (_loadedTemplates == null || _loadedTemplates.Count == 0)
                {
                    _identifyEstado = "error";
                    _identifyError = "No hay templates cargados";
                    _modo = ModoActual.Ninguno;
                    return;
                }

                foreach (var entry in _loadedTemplates)
                {
                    var verifyResult = new DPFP.Verification.Verification.Result();
                    verificator.Verify(featureSet, entry.Template, ref verifyResult);
                    if (verifyResult.Verified)
                    {
                        _identifyEstado = "identificado";
                        _identifySocioId = entry.SocioId;
                        _identifyTemplatePath = entry.Path;
                        _modo = ModoActual.Ninguno;
                        return;
                    }
                }

                _identifyEstado = "no_identificado";
                _identifySocioId = null;
                _identifyTemplatePath = null;
                _modo = ModoActual.Ninguno;
            }
        }

        private string SaveTemplate(DPFP.Template template)
        {
            Directory.CreateDirectory(Config.TemplatesDir);
            var path = Config.TemplatePath(_enrollIdSocio, _enrollDedo, Guid.NewGuid().ToString());
            using (var stream = File.Create(path))
                template.Serialize(stream);
            return path;
        }

        private void LoadTemplates(string[] paths)
        {
            _loadedTemplates = new List<LoadedTemplate>();
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    using (var stream = File.OpenRead(path))
                    {
                        var template = new DPFP.Template(stream);
                        var fileName = Path.GetFileNameWithoutExtension(path);
                        var parts = fileName.Split('_');
                        var socioId = parts.Length > 0 ? parts[0] : "unknown";
                        _loadedTemplates.Add(new LoadedTemplate { SocioId = socioId, Path = path, Template = template });
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _enrollTimer?.Dispose();
        }

        private class LoadedTemplate
        {
            public string SocioId;
            public string Path;
            public DPFP.Template Template;
        }
    }
}

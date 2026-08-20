using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DPFP.Capture;

namespace SmartGym.Biometrics
{
    internal partial class CaptureForm : Form, DPFP.Capture.EventHandler
    {
        internal BiometricEngine Engine;
        internal HttpServer Server;

        private DPFP.Capture.Capture _capturer;
        private DPFP.Processing.Enrollment _enroller;
        private DPFP.Verification.Verification _verificator;

        private static StreamWriter _log;

        private static void Log(string message)
        {
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message;
            try
            {
                var logPath = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath) ?? ".",
                    "sidecar_log.txt");
                if (_log == null)
                {
                    _log = new StreamWriter(logPath, false);
                    _log.AutoFlush = true;
                }
                _log.WriteLine(line);
                _log.Flush();
            }
            catch { }
        }

        public CaptureForm()
        {
            this.Text = "Smart Gym \u2014 Servicio de Huellas";
            this.WindowState = FormWindowState.Normal;
            this.Opacity = 0;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.Size = new Size(250, 120);
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Width - 260,
                Screen.PrimaryScreen.WorkingArea.Height - 140
            );

            var iconPath = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath) ?? ".",
                "icon.ico");
            if (File.Exists(iconPath))
                this.Icon = new Icon(iconPath);
        }

        protected override void OnLoad(EventArgs e)
        {
            Log("OnLoad INICIO");
            base.OnLoad(e);

            Engine = new BiometricEngine(this);
            Log("Engine created");

            _capturer = new DPFP.Capture.Capture();
            Log("new Capture OK");

            _enroller = new DPFP.Processing.Enrollment();
            Log("new Enrollment OK");

            _verificator = new DPFP.Verification.Verification();
            Log("new Verification OK");

            _capturer.EventHandler = this;
            Log("EventHandler assigned");

            _capturer.StartCapture();
            Log("StartCapture() called");

            Server = new HttpServer(Engine);
            Server.Start();
            Log("Server started on " + Config.HttpPrefix);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Log("OnFormClosing");
            Server?.Stop();
            Engine?.Dispose();
            try { _capturer?.StopCapture(); } catch { }
            try { _capturer?.Dispose(); } catch { }
            if (_log != null) { _log.Flush(); _log.Close(); _log = null; }
            base.OnFormClosing(e);
        }

        public void OnReaderConnect(object capture, string readerSerial)
        {
            Log("OnReaderConnect - Serial: " + readerSerial);
            Engine.OnReaderConnect(readerSerial);
        }

        public void OnReaderDisconnect(object capture, string readerSerial)
        {
            Log("OnReaderDisconnect - Serial: " + readerSerial);
            Engine.OnReaderDisconnect(readerSerial);
        }

        public void OnFingerTouch(object capture, string readerSerial)
        {
            Log("OnFingerTouch - Serial: " + readerSerial);
            Engine.OnFingerTouch();
        }

        public void OnFingerGone(object capture, string readerSerial)
        {
            Log("OnFingerGone - Serial: " + readerSerial);
            Engine.OnFingerGone();
        }

        public void OnSampleQuality(object capture, string readerSerial, CaptureFeedback feedback)
        {
            Log("OnSampleQuality - Feedback: " + feedback);
        }

        public void OnComplete(object capture, string readerSerial, DPFP.Sample sample)
        {
            Log("OnComplete - Serial: " + readerSerial + " Sample: " + (sample != null));
            try
            {
                var mode = Engine.Modo;
                Log("Modo: " + mode);

                var purpose = mode == ModoActual.Enrolamiento
                    ? DPFP.Processing.DataPurpose.Enrollment
                    : DPFP.Processing.DataPurpose.Verification;
                var features = ExtractFeatures(sample, purpose);
                Log("ExtractFeatures (" + purpose + "): " + (features != null ? "OK" : "NULL"));

                if (features == null) return;

                if (mode == ModoActual.Enrolamiento)
                {
                    Log("HandleEnrollmentComplete...");
                    Engine.HandleEnrollmentComplete(features, _enroller);
                    Log("HandleEnrollmentComplete done");
                }
                else if (mode == ModoActual.Identificacion)
                {
                    Log("HandleIdentificationComplete...");
                    Engine.HandleIdentificationComplete(features, _verificator);
                    Log("HandleIdentificationComplete done");
                }
                else
                {
                    Log("Modo=Ninguno, sample ignorado");
                }
            }
            catch (Exception ex)
            {
                Log("EXCEPTION OnComplete: " + ex.Message);
                Engine.SetError(ex.Message);
            }
        }

        internal void ClearEnrollmentOnUIThread()
        {
            Log("ClearEnrollmentOnUIThread");
            if (InvokeRequired)
                Invoke((MethodInvoker)(() => { _enroller.Clear(); Log("_enroller.Clear() via Invoke"); }));
            else
            {
                _enroller.Clear();
                Log("_enroller.Clear() direct");
            }
        }

        private DPFP.FeatureSet ExtractFeatures(DPFP.Sample sample, DPFP.Processing.DataPurpose purpose)
        {
            var extractor = new DPFP.Processing.FeatureExtraction();
            CaptureFeedback feedback = CaptureFeedback.None;
            DPFP.FeatureSet features = new DPFP.FeatureSet();
            extractor.CreateFeatureSet(sample, purpose, ref feedback, ref features);
            if (feedback == CaptureFeedback.Good)
                return features;
            Log("ExtractFeatures feedback: " + feedback);
            return null;
        }
    }
}

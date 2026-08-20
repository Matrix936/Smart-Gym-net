using SmartGym.Core.Biometrics;

namespace SmartGym.Tests.Biometrics;

/// <summary>
/// Transiciones de modo puras de BiometricCaptureArbiter — sin SDK, sin
/// hardware. No cubren el flujo real end-to-end (extracción de features,
/// guardado de template, matching 1:N): eso solo se verifica manualmente
/// contra el lector U.are.U 4500 vía /biometric-test. Ver
/// 03-checklist-comportamiento-esperado.md para el detalle de qué queda
/// cubierto aquí y qué solo por prueba manual.
/// </summary>
public sealed class BiometricCaptureArbiterTests
{
    [Fact]
    public void idle_permite_iniciar_enrollment()
    {
        var arbiter = new BiometricCaptureArbiter();

        var ok = arbiter.TryStartEnrollment(out var interrumpio);

        Assert.True(ok);
        Assert.False(interrumpio);
        Assert.Equal(BiometricCaptureMode.Enrolling, arbiter.CurrentMode);
    }

    [Fact]
    public void idle_permite_iniciar_identification()
    {
        var arbiter = new BiometricCaptureArbiter();

        var ok = arbiter.TryStartIdentification();

        Assert.True(ok);
        Assert.Equal(BiometricCaptureMode.Identifying, arbiter.CurrentMode);
    }

    [Fact]
    public void enrolling_rechaza_nuevo_enrollment()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartEnrollment(out _);

        var ok = arbiter.TryStartEnrollment(out var interrumpio);

        Assert.False(ok);
        Assert.False(interrumpio);
        Assert.Equal(BiometricCaptureMode.Enrolling, arbiter.CurrentMode);
    }

    [Fact]
    public void enrolling_rechaza_identification()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartEnrollment(out _);

        var ok = arbiter.TryStartIdentification();

        Assert.False(ok);
        Assert.Equal(BiometricCaptureMode.Enrolling, arbiter.CurrentMode);
    }

    [Fact]
    public void start_enrollment_interrumpe_identificacion_en_espera()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartIdentification();
        // Sin OnFingerTouch: identificacion recien iniciada, nadie tocando el sensor.

        var ok = arbiter.TryStartEnrollment(out var interrumpio);

        Assert.True(ok);
        Assert.True(interrumpio);
        Assert.Equal(BiometricCaptureMode.Enrolling, arbiter.CurrentMode);
    }

    [Fact]
    public void start_enrollment_no_interrumpe_identificacion_en_curso()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartIdentification();
        arbiter.OnFingerTouch(); // dedo puesto: identificacion "en curso"

        var ok = arbiter.TryStartEnrollment(out var interrumpio);

        Assert.False(ok);
        Assert.False(interrumpio);
        Assert.Equal(BiometricCaptureMode.Identifying, arbiter.CurrentMode);
    }

    [Fact]
    public void identificacion_vuelve_a_esperando_cuando_el_dedo_se_retira()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartIdentification();
        arbiter.OnFingerTouch();
        arbiter.OnFingerGone();

        var ok = arbiter.TryStartEnrollment(out var interrumpio);

        Assert.True(ok);
        Assert.True(interrumpio);
    }

    [Fact]
    public void on_finger_touch_y_gone_son_no_op_fuera_de_identifying()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartEnrollment(out _);

        arbiter.OnFingerTouch();
        arbiter.OnFingerGone();

        // No debe afectar el arbitraje de enrollment: sigue ocupado.
        Assert.False(arbiter.TryStartIdentification());
        Assert.Equal(BiometricCaptureMode.Enrolling, arbiter.CurrentMode);
    }

    [Fact]
    public void finish_enrollment_vuelve_a_idle()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartEnrollment(out _);

        arbiter.FinishEnrollment();

        Assert.Equal(BiometricCaptureMode.Idle, arbiter.CurrentMode);
    }

    [Fact]
    public void finish_enrollment_es_no_op_si_no_esta_enrolando()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartIdentification();

        arbiter.FinishEnrollment();

        Assert.Equal(BiometricCaptureMode.Identifying, arbiter.CurrentMode);
    }

    [Fact]
    public void stop_identification_vuelve_a_idle()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartIdentification();

        arbiter.StopIdentification();

        Assert.Equal(BiometricCaptureMode.Idle, arbiter.CurrentMode);
    }

    [Fact]
    public void stop_identification_es_no_op_si_no_esta_identificando()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartEnrollment(out _);

        arbiter.StopIdentification();

        Assert.Equal(BiometricCaptureMode.Enrolling, arbiter.CurrentMode);
    }

    [Fact]
    public void despues_de_interrumpir_identificacion_un_segundo_enrollment_es_rechazado()
    {
        var arbiter = new BiometricCaptureArbiter();
        arbiter.TryStartIdentification();
        arbiter.TryStartEnrollment(out var primeraInterrupcion);

        var ok = arbiter.TryStartEnrollment(out var segundaInterrupcion);

        Assert.True(primeraInterrupcion);
        Assert.False(ok);
        Assert.False(segundaInterrupcion);
    }
}

# 04 — Integración Biométrica (sidecar reutilizable)

**Fuente:** `docs/investigacion-sidecar-biometrico.md` (25 julio 2026) y `docs/arquitectura/00-decisiones-tecnicas-smart-gym.md §5`.

**Regla de rescate:** el sidecar **ya es C#/.NET** (`SmartGym.Biometrics.exe`) — es la pieza que se reutiliza tal cual en la migración, sin reescribir. Este documento congela los hallazgos para que no se pierdan y para integrar el sidecar desde el primer día.

---

## 1. Stack del sidecar

| Aspecto | Valor |
|---|---|
| SDK | DigitalPersona One Touch SDK v1.6.1.0 (.NET Framework 4.8) |
| Ensamblados | `DPFPDevNET.dll`, `DPFPEngNET.dll`, `DPFPGuiNET.dll`, `DPFPShrNET.dll`, `DPFPVerNET.dll` |
| Lector | U.are.U 4500 Fingerprint Reader (USB, driver `usbdpfp`) |
| Plataforma | .NET Framework 4.8, WinExe, AnyCPU |
| Patrón | `Form` implementa `DPFP.Capture.EventHandler`, posee `Capture`, `Enrollment`, `Verification` |
| Ubicación SDK | `C:\Program Files\DigitalPersona\One Touch SDK`, `C:\Program Files\DigitalPersona\Bin` |

**Proyecto anterior:** la versión MAUI del proyecto ya integró `DPFP.Capture.EventHandler` como clase de servicio sin `Form` — funcionaba solo porque MAUI daba implícitamente STA thread + message loop **y** WbioSrvc estaba corriendo. Eso se rompe si WbioSrvc está detenido (ver §3).

---

## 2. Arquitectura de integración

```
[Tauri/.NET App] ⇄ (HTTP localhost) ⇄ [SmartGym.Biometrics.exe] ⇄ (OneTouch SDK) ⇄ [U.are.U 4500]
```

- El sidecar expone una API local mínima (HTTP en `localhost`, puerto fijo): health, iniciar captura/enrolamiento, identificación 1:N, estado en tiempo real.
- La app lanza y cierra el sidecar (al entrar/salir del modo Kiosco o Control de Acceso).
- **Separación de responsabilidades:**
  - El sidecar **solo habla con el hardware** (captura, calidad, match).
  - La app **decide la lógica de negocio**: ¿membresía activa?, ¿acceso permitido?, registrar bitácora.
- `socios_biometricos` guarda la ruta del template (`.bin`, 1632 bytes), es local-only y su única red de seguridad es el backup local cifrado.

---

## 3. Causa raíz documentada (NO reintroducir)

El SDK tiene **dos paths** según el estado de `WbioSrvc` (Windows Biometric Service):

| Configuración | WbioSrvc Stopped | WbioSrvc Running |
|---|---|---|
| `WindowState.Minimized` | ❌ No funciona | ✅ Funciona |
| `WindowState.Normal` visible | ✅ Funciona | ✅ Funciona |

- **Path WBF** (WbioSrvc corriendo): el SDK delega al servicio biométrico de Windows; no requiere ventana visible.
- **Path directo** (WbioSrvc detenido): usa el driver `usbdpfp`; **REQUIERE** ventana visible en `WindowState.Normal` durante TODO el ciclo de vida del `Capture`. Minimizar después del arranque mata los callbacks.

### Decisiones de diseño ya tomadas (respetar)

1. **Ventana visible permanente** (`WindowState.Normal`, `ShowInTaskbar=true`, `Size=300×200`): **aplica solo al sidecar Tauri standalone, NO al puerto MAUI.**

   > **Actualizado y verificado con hardware real — 2026-08-20.** Prototipo aislado `SmartGym.App/BiometricPrototype/` (`/biometric-test`, ver `07-lecciones-de-proceso.md` para la metodología). `DPFP.Capture.EventHandler` implementado como servicio simple embebido en el proceso MAUI/WinUI3, **sin ningún `Form` WinForms, sin `WindowState` forzado, sin `Activate()` marshalado, sin foco manual**. Dos corridas independientes con lector U.are.U 4500 real, `WbioSrvc` **Stopped** (la condición que antes exigía la ventana visible):
   >
   > | Evento | Corrida 1 | Corrida 2 (arranque en frío, build limpio) |
   > |---|---|---|
   > | `OnReaderConnect` | 1 | 1 |
   > | `OnFingerTouch` | 14 | 7 |
   > | `OnComplete` (Sample: True) | 9 | 6 |
   > | `OnFingerGone` | 14 | 7 |
   > | Excepciones | 0 | 0 |
   >
   > Touch = Gone en ambas corridas (sin callbacks perdidos), cero excepciones. **Conclusión: en MAUI/WinUI3 + WebView2 el message pump COM que requiere el SDK ya existe de forma implícita (STA thread + message loop del proceso), igual que se documentaba para la versión MAUI previa (§1). La ventana WinForms visible NO se porta al módulo real.**
   >
   > Único bloqueo encontrado (no relacionado con ventana/foco): `DPFP.Capture.Capture.MessageEvents.EnsureInitialized()` requiere el ensamblado `System.Windows.Forms` en tiempo de ejecución aunque no se use ningún `Form`. Activar `UseWindowsForms=true` rompe el build (`MC6000`, conflicto del markup compiler WPF de `Microsoft.NET.Sdk.WindowsDesktop` con los `.xaml` de WinUI3/MAUI). Solución: `<FrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms" />` en el `.csproj` — trae el ensamblado sin activar el SDK de escritorio completo.
   >
   > No cubierto por esta prueba: enrollment/verification real, prueba con `WbioSrvc` corriendo (control comparativo), estabilidad bajo carga sostenida (hipótesis de contención de hilos, §6 — mitigada de raíz porque el módulo real no necesita servidor HTTP local, ver nota en §6).

2. **No depender de WbioSrvc** — el sidecar no verifica ni inicia el servicio (puede estar deshabilitado por política corporativa). Esto se mantiene también en el módulo MAUI: la prueba se hizo justamente con el servicio detenido.
3. **Costo aceptado (solo aplica al sidecar Tauri standalone)**: icono pequeño permanente en la barra de tareas. No aplica al módulo MAUI embebido — no hay proceso ni icono separado.

---

## 4. Bug corregido: DataPurpose hardcodeado

- **Síntoma:** el enrollment fallaba en el 4.º toque (`Enrollment procedure failed`).
- **Causa:** `OnComplete` extraía features siempre con `DataPurpose.Verification`.
- **Corrección** (patrón obligatorio en el sidecar):

```csharp
var purpose = mode == ModoActual.Enrolamiento
    ? DPFP.Processing.DataPurpose.Enrollment
    : DPFP.Processing.DataPurpose.Verification;
var features = ExtractFeatures(sample, purpose);
```

- **Resultado:** enrollment completo en 4 toques; template `.bin` de 1632 bytes.

---

## 5. Hallazgo: `Activate()` marshalado al hilo UI durante el cambio de modo

En `TrySetModoEnrolamiento` / `TrySetModoIdentificacion`, el cambio de modo **activa la ventana** (`Form.Activate()`) en el hilo de UI. Este `Activate()` marshalado es parte del mecanismo que hace funcionar la captura con ventana visible — con o sin foco previo del usuario.

**Qué implica:**

- No basta con que la ventana exista en `WindowState.Normal`: el cambio de modo debe **reactivar la ventana vía el hilo de UI** (marshal de un hilo de trabajo al hilo del message loop de la ventana, p. ej. `Invoke`/`BeginInvoke`), no llamando `Activate()` directamente desde un hilo de servidor/trabajo.
- Esto refuerza la conclusión de §3 en el sentido de que el **message pump COM en el hilo de la ventana** es el prerequisito real de los callbacks del SDK — la visibilidad es el vehículo que garantiza que ese pump exista y se procese.
- Corregir un "no detecta huellas" en el futuro mínimo viable: verificar que el modo actual se haya seteado **desde el hilo de UI** y que la ventana esté activada, antes de sospechar del hardware.

**Regla para el port a .NET:** cualquier operación que cambie de modo (enrolamiento ↔ identificación) debe tocar la ventana (activar/poner al frente) mediante un dispatch al hilo de UI de la ventana, no desde el hilo del `HttpListener`/servidor local.

---

## 6. Hipótesis de investigación abierta: contención de hilos (HttpListener/ThreadPool vs. message pump COM)

**Estado:** hipótesis abierta, **no confirmada** — pero la vía recomendada (sin servidor HTTP local) ya se validó con hardware real, ver §3.1.

El síntoma "funciona a veces / deja de funcionar" podría explicarse por **contención de hilos**: los hilos del `ThreadPool` que atienden las peticiones del `HttpListener` compiten o "roban" tiempo/prioridad al message pump COM que el SDK requiere para entregar `OnFingerTouch`/`OnComplete`. Cuando el pump no se despacha a tiempo, los callbacks se pierden o se entregan tarde.

**Por qué importa para .NET:**
- Si se mantiene la arquitectura de sidecar con servidor HTTP local, **existe el mismo riesgo** de que los hilos del servidor interfieran con el pump COM — la hipótesis se debe volver a evaluar si reaparece el síntoma.
- **El servidor HTTP local ahora es opcional:** en MAUI el SDK podría residir en el proceso principal y las operaciones invocarse directamente (sin HTTP), lo que elimina de raíz la superficie de contención. Esa es una vía de diseño recomendada para la migración.

**Si el mismo síntoma reaparece en .NET:** medir si los callbacks llegan cuando el servidor local está inactivo vs. bajo carga; aislar si la activación de la ventana en el cambio de modo "despierta" el pump; documentar antes de parchear.

---

## 7. Patrón de auto-arranque bajo demanda del sidecar

El sidecar **no se lanza al iniciar la app**. Se lanza bajo demanda, disparado por la acción del usuario:

1. La acción del usuario abre **Kiosco** o una pantalla de **enrolamiento**.
2. Se consulta el health endpoint (`GET /health`) con un timeout corto.
3. Si responde → se reutiliza la instancia ya corriendo.
4. Si no responde → se lanza el sidecar (vía `Process.Start`, ver §8) y se espera a que el health endpoint responda antes de declarar el sidecar listo.
5. El sidecar puede cerrarse al salir del Kiosco / cerrar la pantalla de enrolamiento (si no hay más operaciones pendientes).

**Justificación:** arrancar el sidecar siempre al abrir la app desperdicia recursos, ocupa la barra de tareas sin necesitarlo y añade un punto de fallo en cada arranque. Con el patrón bajo demanda el sidecar solo existe cuando alguien lo va a usar, y el "arrancar + verificar health" es determinista para el flujo que necesita el hardware.

**Reglas:**
- Todo el arranque bajo demanda se dispara desde una acción de usuario (nunca en segundo plano al abrir la app).
- Timeout de health corto + reintento limitado; si el sidecar no responde tras el intento, mostrar error claro y permitir reintentar (misma regla que el Kiosco: nunca quedarse colgado).

---

## 8. Lanzamiento del sidecar: usar `Process.Start`, NO plugin shell

El lado Rust descubrió que `tauri-plugin-shell` rompe el arranque del sidecar:

- Resuelve la ruta como `current_exe().parent() / <externalBin>` (busca en `target/debug/binaries/`, que no existe en dev mode).
- Aplica `CREATE_NO_WINDOW` incondicionalmente, que impide la inicialización de GUI WinForms.

**Solución correcta (portar a .NET):** lanzar con `System.Diagnostics.Process.Start` resolviendo el binario desde el directorio de recursos de la app, tanto en dev como en producción. Verificar el health endpoint antes de declarar el sidecar listo (ver §7).

---

## 9. Checklist de diagnóstico (si "no detecta huellas")

1. **No asumas que es código** — verificar `Get-Service WbioSrvc` primero.
2. El sidecar NO necesita WbioSrvc (diseñado para funcionar sin él).
3. Si se quiere probar con WbioSrvc: `Start-Service WbioSrvc` (opcional).
4. **La ventana DEBE ser visible** — no minimizar, no mover off-screen, no cambiar `ShowInTaskbar` en runtime. (Recordar: los cambios de modo deben reactivar la ventana vía hilo UI — ver §5.)
5. Verificar driver del sensor: `Get-PnpDeviceProperty -KeyName DEVPKEY_Device_Service` → `usbdpfp`.
6. Si el sensor no responde: reconectar USB, esperar 15s, verificar driver, relanzar sidecar.

---

## 10. Requisitos de la app .NET para el Kiosco

- El comando de contexto Kiosco (identificar por huella / registrar acceso) **no pasa por `validar_sesion`**, pero valida el contexto de modo Kiosco (flag interno, no expuesto al frontend).
- El Kiosco nunca expone membresías, finanzas ni datos personales más allá del nombre/foto para confirmar identidad visual.
- El enrolment solo desde sesión administrativa con `socios.editar`.

---

## 11. Comportamientos verificados en los tests (referencia)

Los tests `biometrics.rs` (ver `03-checklist-comportamiento-esperado.md`) verifican el parsing de respuestas del sidecar y la lógica de selección de templates por sede/membresía (activa → aparece; vencida → no; congelada → aparece en templates pero el acceso se deniega en `access.rs`). Portar estos tests garantiza que el contrato de comunicación con el sidecar no se rompa.
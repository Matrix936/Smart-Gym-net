# 08 — Problemas resueltos y patrones a respetar (Blazor + MudBlazor)

**Propósito:** catálogo de los problemas reales que se resolvieron durante el port a .NET MAUI + Blazor Hybrid (MudBlazor 9.8.0), con su causa raíz y la solución, para **consultar cada vez que surja un problema** y para **evitar repetirlos** en el código nuevo. Antes de tocar código ante un bug, revisar si ya está documentado aquí.

**Convención:** si un problema se repite y tiene una regla preventiva, se marca con **REGLA**.

---

## 0. Comandos y flujo de build (LEER PRIMERO)

- Solución: `C:\Users\freea\Proyectos\smart-gym-dotnet`.
- Build: `dotnet build SmartGym.App\SmartGym.App.csproj` (workdir = raíz de la solución).
- Tests: `dotnet test` (actualmente **154**).
- Relanzar: `Start-Process -FilePath "...\SmartGym.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\SmartGym.App.exe"`.
- **REGLA (MSB3026/MSB3021):** si la app está abierta, el exe bloquea las DLLs y el build falla con "The process cannot access the file... retries". **Siempre** `Stop-Process -Name SmartGym.App -Force` (esperar ~1s) **antes** de buildear.
- **REGLA:** nunca usar `dotnet build -t:Run`; lanzar el exe con `Start-Process`.
- Al terminar un cambio: build 0 errores → `dotnet test` → relanzar la app.

---

## 1. Ruteo: página sin `@page` → "Not Found"

- **Síntoma:** el componente compila sin errores pero la ruta responde 404 / "Sorry, the content you are looking for does not exist".
- **Causa:** `MiembrosPage.razor` se creó sin la directiva `@page "/miembros"`. El componente era válido (no fallaba el build) pero el `Router` (`AppAssembly="typeof(MauiProgram).Assembly"`) no registraba ninguna ruta.
- **Solución:** `@page "/miembros"` como primera directiva.
- **REGLA:** al crear cualquier página, poner `@page "/..."` en la **primera** línea de directivas y verificar que la ruta exista en el NavMenu. Si un módulo da 404, revisar primero que tenga `@page`.

---

## 2. MudSelect con tipo anulable (`T="long?"`) → crash al seleccionar

- **Síntoma:** al seleccionar un item del `MudSelect` aparece el overlay "An unhandled error has occurred. Reload".
- **Causa:** bug conocido de MudBlazor (issue #3922): `MudSelect T="long?"` con items que infieren `T="long"` → `InvalidCastException` ("Unable to set property 'IMudShadowSelect'... Specified cast is not valid") al registrar/seleccionar el item.
- **Solución:** usar **siempre el mismo tipo no anulable** en `MudSelect` y `MudSelectItem`, con **centinela** para "sin selección":
  - `T="long"` + `private long _sedeSeleccionada;` donde `0 = sin selección` (los ids son AUTOINCREMENT desde 1).
  - Validación con `<= 0`, envío con `_showSede ? _sedeSeleccionada : null` (long → long? implícito).
- **REGLA:** nunca usar tipos anulables (`int?`, `long?`, enums?) como `T` de `MudSelect` con items del tipo no anulable. Usar tipo no anulable + sentinela.

---

## 3. MudTooltip en items del sidebar rompe el layout

- **Síntoma:** los módulos del sidebar aparecían en **una sola línea** en vez de apilados.
- **Causa:** `MudTooltip` envuelve su hijo en `<div class="mud-tooltip-root mud-tooltip-inline">` con `display: inline-block`, rompiendo el flex del `.sg-nav`.
- **Solución:** quitar `MudTooltip` del sidebar y usar el atributo nativo `title` (además hay wrapper `.sg-nav` con scroll).
- **REGLA:** no usar `MudTooltip` como wrapper de items de layout flex (sidebar, menús). Para tooltips del sidebar usar `title`.

---

## 4. Highlight activo del sidebar con retraso / "dos clics"

- **Síntoma:** al cambiar de módulo, el item activo se resaltaba con retraso o recién al segundo clic.
- **Causa:** `EsActivo()` leía `NavigationManager.Uri` **durante el render**. En el WebView de MAUI la URI se aplica de forma **asíncrona** respecto al clic, así que el primer re-render post-evento usaba la URI vieja; el resaltado aparecía solo cuando algo re-renderizaba el NavMenu después (segundo clic o el timer de 1s del reloj).
- **Solución (ya aplicada en `NavMenu.razor`):**
  - `@implements IDisposable`.
  - Campo `_rutaActual`, suscripción en `OnInitialized()`:
    `Nav.LocationChanged += OnLocationChanged;`
  - Handler: `_rutaActual = RutaNormalizada(e.Location); InvokeAsync(StateHasChanged);`
  - `EsActivo(href)` compara contra `_rutaActual`; `Dispose()` desuscribe.
- **REGLA:** el estado "activo" de la navegación **nunca** debe depender de leer `Nav.Uri` en el render. Suscribirse a `NavigationManager.LocationChanged` y cachear la ruta.

---

## 5. N+1: indicador de huella por fila

- **Síntoma (perf):** la lista de Miembros hacía una consulta SQL por socio (`ExisteHuellaAsync`) para el indicador de huella.
- **Solución:** método batch `ISociosBiometricosRepository.GetIdsConHuellaAsync()` (una sola query `SELECT DISTINCT id_socio ...`) y en la página un `HashSet<string>` (`_sociosConHuella`) para membership O(1).
- **REGLA:** evitar consultas N+1 en listados. Si hay que mostrar un flag calculado por fila, agregar un método batch al repositorio y resolver con un `HashSet`.

---

## 6. Diálogos de formulario (crear/editar) — patrones

- **Guardado del botón confirmar:** `AsyncButton` con `OnClick="GuardarAsync"` + **guard de reentrada** `if (_guardando) return;`. ~~Envolver el `<MudDialog>` en `<form @onsubmit>`~~ — era código muerto: los `DialogContent`/`DialogActions` de un `MudDialog` **inline se teleportan al `MudDialogProvider`**, así que nunca quedan dentro del `<form>` del padre.
- **Selector de sede solo para SUPERADMIN:** mostrar solo si `Sesion.Current?.IdSede is null`; en ese caso **preseleccionar la sede principal** (`Sedes.GetPrincipalAsync()`, fallback a la primera activa).
- **Sede inactiva al editar:** si `Socio.IdSedeRegistro` no está entre las sedes activas, agregarla a la lista (`Sedes.GetByIdAsync`) para que el selector no quede vacío.
- **Validación client-side:** nombre y apellido paterno requeridos; sede si el selector está visible; email con `Contains('@')`; fecha con `DateTime.TryParse`. Snackbar `Feedback.ShowWarning(...)` (consistente con Login/SetupWizard).
- **Fecha de nacimiento:** el input `InputType="Date"` exige `yyyy-MM-dd`; al editar normalizar con `(Socio.FechaNacimiento ?? "").Length >= 10 ? ...[..10] : ...`.
- **Modal montado con `@if`** → `OnInitializedAsync` corre fresco en cada apertura; no confiar en re-uso del componente.
- **REGLA:** formularios con `MudDialog` + `@if` en el padre + init en `OnInitializedAsync` + `OnParametersSet` con flag `_inicializado` solo como respaldo.
- **Cierre del diálogo (RESUELTO — Bug "no se puede salir con Cancelar/backdrop"):** un `<MudDialog Visible="...">` inline (sin `DialogInstance` a través de `@bind-Visible` en el provider) tiene `IsInline=true` y en `OnAfterRenderAsync` llama `ShowAsync()` → el contenido se renderiza en el `MudDialogProvider` (`AppShell.razor`). El **desmontaje `@if` del padre NO cierra el diálogo real**; solo cerrarlo con `Visible=false` dispara `CloseAsync()` → `_reference.Close()` → el provider lo descarta. Patrón robusto:
  - `<MudDialog @ref="_mudDialog" Visible="_visible" VisibleChanged="OnVisibleChanged">` con `BackdropClick = true` y `CloseOnEscapeKey = true`.
  - `_visible = Open` en `OnInitializedAsync`/`OnParametersSet`; `Cancelar()` y el éxito de guardar hacen `await _mudDialog.CloseAsync()` (descarta el reference del provider de forma síncrona).
  - `OnVisibleChanged` sincroniza `_visible` y, si `!visible`, llama `OnClose` (cubre backdrop/Escape que vienen del container del provider). ConfirmDialog ya usaba este patrón (por eso cerraba bien).

---

## 7. AccesoManualDialog — alertas (ManualAccessDialog.tsx)

- **Banner informativo** (`access-alert-info`): "Marcar manualmente la entrada de *X*. Si la membresía está vencida o el socio bloqueado, se registrará el acceso como **denegado**."
- **Alerta de resultado** (`access-result-ok/ko`): "Acceso registrado correctamente" / "Acceso denegado" + motivo.
- **Snackbar de error** cuando el resultado es denegado (con el label del motivo).
- **Botón "Registrar acceso" deshabilitado** una vez que hay resultado (`Disabled="_resultado is not null"`).
- Motivos: `socio_bloqueado`, `socio_inactivo`, `membresia_vencida`, `membresia_congelada` (labels es-MX en `AccesoManualDialog.razor`).

---

## 8. Sesión persistente y localStorage

- Token persistido en `AppData\Roaming\Smart-Gym-net\sesion.token`; `Auth.RestaurarSesionAsync()` valida contra DB (12h) en el arranque.
- Claves de UI en localStorage: `sidebar-open` (`"true"` = colapsado) y `sidebar-collapsed-groups` (JSON array de labels cerrados).
- **REGLA:** para cualquier persistencia nueva de UI usar localStorage vía `IJSRuntime`, con `try/catch` (puede no estar disponible).

---

## 9. Iconos solar line-duotone (Iconify)

- Embebidos como SVG en `SmartGym.App\Services\SolarIconos.cs` (© 480-design, licencia CC BY 4.0 — conservar atribución). Render vía `IconifyIcon.razor` (`MarkupString` sobre strings estáticos de confianza), 24px.
- Fórmula duotone: `stroke="currentColor"` ancho 1.5; acentos con `opacity=".5"/".4"`.
- Indicador activo separado del icono: `.sg-nav-item { padding-left: 8px }` (no meter el indicador dentro del span del icono).
- **REGLA:** si se agrega un icono nuevo, agregar el SVG real al catálogo; no aproximar con otra familia (las del catálogo ya están alineadas al diseño del base).

---

## 10. Autorización / permisos

- `SociosService`/`AccesoService` validan permisos **server-side** con el token (`RequierePermisoAsync`). El front **no** hace gating por permiso (el `SessionInfo` no trae lista de permisos).
- **REGLA:** no intentar ocultar acciones por rol en el front; dejar que el servicio rechace con `BusinessException`.

---

## 11. Registro de huella (enrollment) — NO portable a .NET por ahora

- El `FingerprintEnrollDialog` del base depende de lector de hardware + sidecar Tauri (`enrollment:status`).
- En el port la columna "Huella" es solo un **indicador read-only** (`GetIdsConHuellaAsync`); el item "Registrar huella" del menú no se portó.
- Ver `04-integracion-biometrica.md` y `07-lecciones-de-proceso.md` para el contexto de hardware.

---

## 12. Errores / feedback (patrón global)

- Canal global `IFeedbackService` → `FeedbackSnackbar` (montado en `AppShell`). **Nunca** alertas locales ad-hoc.
- `catch (BusinessException ex) { Feedback.ShowError(ex.Message); }` + `catch { ... genérico ... }` + `finally`.
- Acciones destructivas siempre con `ConfirmDialog` compartido (patrón `_confirmar` = record `{ Titulo, Mensaje, TextoConfirmar, Func<Task> Accion }`, `IsLoading` mientras corre, limpiar en `finally`).

---

## Checklist rápido ante un bug nuevo

1. ¿Está documentado aquí? (build, ruteo, MudSelect, tooltip, highlight, N+1, formularios, feedback).
2. Build: ¿la app estaba corriendo? (matar antes de buildear).
3. Ruta: ¿la página tiene `@page`?
4. MudSelect: ¿usé tipo anulable como `T`?
5. ¿Lectura de `Nav.Uri` en render para estado activo?
6. ¿Consultas N+1 en listados?
7. ¿El diálogo cierra con `_mudDialog.CloseAsync()` (no solo `@if` del padre) y tiene guard de reentrada? ¿`BackdropClick = true` y `VisibleChanged` → `OnClose`?
8. ¿Feedback por `IFeedbackService` y confirmaciones con `ConfirmDialog`?
9. Verificar con build 0 errores + `dotnet test` + relanzar app.
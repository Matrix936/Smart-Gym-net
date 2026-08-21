# 09 — Auditoría SOLID (Miembros + servicios de dominio)

**Fecha:** 2026-08-20. **Alcance:** `SmartGym.Core`, `SmartGym.Data`, `SmartGym.App` — auditoría de solo lectura sobre el estado del código en ese momento, no un checklist genérico de "buenas prácticas". Cada hallazgo se evaluó por impacto real (bug activo, riesgo para módulos futuros, costo de tests) antes de decidir si se corregía.

**Criterio de cierre:** se corrigió lo que era barato y de alto riesgo real (#1, #2). Lo demás queda documentado como deuda técnica **consciente** — evaluada y descartada por ahora, no ignorada — con un gatillo explícito de cuándo revisarla. Mismo criterio que `04-integracion-biometrica.md` para sus pendientes.

---

## Resumen

| # | Hallazgo | Principio | Estado | Prioridad original |
|---|---|---|---|---|
| 1 | `AccesosRepository` switch sin `SocioEstados.Suspendido` | OCP/DIP | ✅ Corregido | Alta |
| 2 | `ResolverIdSedeAsync` duplicado y divergente (5 servicios) | SRP | ✅ Corregido | Alta |
| 3 | `RegistrarBitacora` duplicado idéntico (5 servicios) | SRP/DRY | 📋 Deuda consciente | Media |
| 4 | `BiometricCaptureService` concentra 5 responsabilidades | SRP | 📋 Deuda consciente (decisión ya tomada) | Baja |
| 5 | `IBiometricCaptureService` sin segregar enrolamiento/identificación | ISP | 📋 Deuda consciente | Media |
| 6 | `NombreCompleto` reimplementado 4 veces | DRY | 📋 Deuda consciente | Baja |

---

## #1 — `AccesosRepository`: switch de decisión de acceso sin `Suspendido` ✅ Corregido

**Commit:** [`c3df0ef`](https://github.com/Matrix936/Smart-Gym-net/commit/c3df0ef) — `fix(accesos): SocioEstados.Suspendido caia en el default del switch de acceso`.

**Qué era:** `AccesosRepository.RegistrarAsync` tenía un `switch (socio.Estado)` que solo manejaba explícitamente `"bloqueado"` e `"inactivo"`. Cualquier otro valor —incluido `"suspendido"`, un estado válido de `SocioEstados.Validos`— caía en el `default` y se evaluaba por membresía como si el socio estuviera activo. Sin cobertura de tests. Era la operación central de Kiosco (`RegistrarKioskoAsync`), módulo aún no construido.

**Corrección:** lógica extraída a `AccesoDecisor.Decidir(estadoSocio, estadoMembresiaVigente)` (`SmartGym.Core/Entities/AccesoBitacora.cs`) — pura, sin Dapper/SQLite, testeable sin transacción. Maneja explícitamente los 4 estados de `SocioEstados.Validos`; cualquier estado no reconocido lanza `BusinessException.Validation` en vez de aprobar implícitamente. Nuevo `MotivosDenegacionAcceso` (antes strings sueltos repetidos). 14 tests nuevos (13 de matriz pura + 1 end-to-end).

**Verificado:** build 0 errores, 178/178 tests tras el fix (164 previos + 14 nuevos).

---

## #2 — `ResolverIdSedeAsync` duplicado y divergente en 5 servicios ✅ Corregido

**Commit:** [`c9eeba8`](https://github.com/Matrix936/Smart-Gym-net/commit/c9eeba8) — `fix(sedes): unifica ResolverIdSedeAsync duplicado y divergente en 5 servicios`.

**Qué era:** `SociosService`, `CajaService`, `MembresiasService`, `PosService` y `CobranzaService` reimplementaban cada uno su propio `ResolverIdSedeAsync` privado, y las 5 copias **no eran equivalentes**:

- `CajaService`: validaba existencia **y** `EsActiva` (la variante correcta).
- `SociosService`: solo validaba existencia, no `EsActiva`.
- `MembresiasService`, `PosService`, `CobranzaService`: no validaban la sede en absoluto — solo verificaban indirectamente que hubiera una caja abierta ahí.

El problema alcanzaba a **4 de los 5** servicios, no solo a los 3 que parecían más obvios en la auditoría original (esa estimación inicial quedó corregida al implementar el fix).

**Corrección:** `ISedeResolutionService`/`SedeResolutionService` nuevo en `SmartGym.Core.Services` — único punto de la regla "la sesión local gana sobre el id_sede del frontend; la sede debe existir y estar activa" (la variante de `CajaService`, aplicada uniformemente). Las 5 implementaciones privadas se eliminaron por completo. `MembresiasService`/`PosService`/`CobranzaService` conservan su propia verificación de "caja abierta" (responsabilidad distinta, no tocada), ejecutada después de validar la sede.

**Cambio de comportamiento real e intencional:** sede inactiva ahora se rechaza (`sede_invalida`) en los 5 servicios — antes se permitía en 4 de ellos.

**Tests existentes ajustados por el cambio de comportamiento: 0.** La suite completa (178/178) pasó sin fallos inmediatamente después de reemplazar las 5 copias, antes de agregar ningún test nuevo — ningún test dependía del comportamiento divergente viejo. Confirma que la divergencia estaba completamente sin probar en la práctica, no que hubiera un comportamiento "deseado" documentado en otro lado.

**Tests nuevos:** 5, uno por servicio (`create_member_sede_inactiva_es_rechazada`, `abrir_caja_sede_inactiva_es_rechazada`, `vender_membresia_sede_inactiva_es_rechazada`, `registrar_venta_sede_inactiva_es_rechazada`, `registrar_abono_sede_inactiva_es_rechazada`).

**Verificado:** build 0 errores, 183/183 tests tras el fix (178 previos + 5 nuevos).

---

## #3 — `RegistrarBitacora` duplicado idéntico en 5 servicios 📋 Deuda consciente

**Ubicaciones:** `SociosService`, `CajaService`, `MembresiasService`, `PosService`, `CobranzaService` — cada uno con su propio método privado `RegistrarBitacora(SessionInfo, ...)` que construye un `BitacoraAuditoria`. A diferencia del #2, el código es **idéntico** entre copias, no divergente.

**Por qué no se corrige ahora:** es deuda real (SRP/DRY) pero de **riesgo bajo mientras el código no diverja** — a diferencia del #2, hoy no hay ningún comportamiento incorrecto en producción que corregir, solo repetición mecánica. Extraerlo es barato pero no urgente: no hay un bug esperando a que alguien lo pise.

**Gatillo para revisarlo:**
- Se agrega un campo obligatorio o una regla nueva a `BitacoraAuditoria` (ej. un hash de integridad) — en ese momento hay que tocar 5 sitios y el riesgo de que diverjan (como pasó con #2) sube de golpe.
- Antes de construir Kiosco, si su código va a reimplementar el mismo patrón de auditoría — mejor extraerlo una vez que reimplementarlo por sexta vez.

---

## #4 — `BiometricCaptureService` concentra 5 responsabilidades 📋 Deuda consciente (decisión ya tomada)

**Ubicación:** `SmartGym.App/Services/BiometricCaptureService.cs` (475 líneas). Mezcla: traducción de eventos del SDK DPFP, gestión de sesión de enrolamiento, gestión de sesión de identificación 1:N, I/O de archivos de templates, y logging a disco.

**Por qué no se corrige ahora — decisión de diseño ya tomada y reafirmada, no una omisión:** el proyecto ya evaluó conscientemente si valía la pena una interfaz adicional para aislar el SDK real y decidió que no. `03-checklist-comportamiento-esperado.md` (línea 169) documenta explícitamente que `BiometricCaptureService` "está fuertemente acoplado al SDK DigitalPersona real... No hay mock del SDK", y en su lugar se extrajo `BiometricCaptureArbiter` (lógica pura, 119 líneas, 13 tests) para obtener testabilidad **sin** tocar la ruta ya validada con hardware real (dos corridas con el lector U.are.U 4500, `04-integracion-biometrica.md` §3.1). Esta auditoría reafirma esa decisión: no se propone abstraer el SDK.

**Lo único nuevo que aporta esta auditoría:** el logging (`Log()`, `File.AppendAllText` bajo lock propio) es la única de las 5 responsabilidades que no depende del SDK y podría extraerse sin tocar la ruta validada — pero no hay presión para hacerlo (es estable, sin bugs reportados).

**Gatillo para revisarlo:** solo si el logging mismo empieza a causar fricción (ej. se necesita logging estructurado o remoto) — en ese caso extraerlo aislado del resto, no como parte de una refactorización más amplia de la clase.

---

## #5 — `IBiometricCaptureService` sin segregar enrolamiento/identificación 📋 Deuda consciente

**Ubicación:** `SmartGym.Core/Biometrics/IBiometricCaptureService.cs`. `FingerprintEnrollDialog.razor` (Bloque 3) inyecta la interfaz completa pero solo usa `CurrentMode`, `StartEnrollmentAsync`, `CancelEnrollment` y `EnrollmentStatusChanged` — nunca los métodos de identificación.

**Por qué no se corrige ahora:** el diseño original (`CurrentMode` único, arbitraje "esperando cede, en curso no se interrumpe") es correcto porque enrolamiento e identificación compiten por el mismo lector físico — eso no se cuestiona. Lo que no se decidió explícitamente en esa ronda es si el contrato debía exponerse como una interfaz o como dos segregadas (`IEnrollmentCaptureService` + `IIdentificationCaptureService`) implementadas por la misma clase concreta, compatible con mantener un único arbitraje interno vía DI. No es urgente porque Kiosco —el único consumidor futuro de los métodos de identificación— todavía no existe.

**Gatillo para revisarlo:** antes de escribir el primer consumidor de identificación (es decir, al arrancar Kiosco). Es más barato decidir la forma de la interfaz antes de tener un segundo consumidor real que refactorizarla después.

---

## #6 — `NombreCompleto` reimplementado 4 veces 📋 Deuda consciente

**Ubicaciones:** `AuthService.cs`, `MiembrosPage.razor`, `AccesoManualDialog.razor`, `FingerprintEnrollDialog.razor` — cada uno decide independientemente cómo tratar nombres/apellidos nulos o vacíos al concatenar.

**Por qué no se corrige ahora:** cosmético, sin riesgo de datos. Las 4 versiones producen hoy el mismo resultado.

**Gatillo para revisarlo:** si `ApellidoMaterno` (u otro campo del nombre) pasa a tratarse de forma realmente distinta en alguna pantalla (ej. opcional de verdad, no solo `""` por defecto) — ahí alguna de las 4 copias podría divergir visualmente (espacio doble, etc.) sin que sea evidente cuál.

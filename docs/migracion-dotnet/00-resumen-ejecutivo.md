# 00 — Resumen Ejecutivo — Migración Smart Gym a .NET

**Fecha snapshot:** 2026-08-09
**Propósito:** Punto de partida único para la reimplementación de Smart Gym en .NET (MAUI + Blazor Hybrid). Esta carpeta congela el estado actual del proyecto para que la migración se haga contra hechos verificados, no contra memoria.

---

## 1. Snapshot del sistema actual

| Aspecto | Valor verificado | Fuente |
|---|---|---|
| Runtime / empaquetado | Tauri v1, app de escritorio | `docs/arquitectura/00-decisiones-tecnicas-smart-gym.md` |
| Backend | Rust modular (dominios), sin ORM, SQLite vía `rusqlite` | ídem |
| Frontend | React + MUI 6 (Material UI) + Vite + Hanken Grotesk | `docs/arquitectura/07-diseno-ui-smart-gym.md` |
| Base de datos local | SQLite, **30 tablas**, 20 triggers, 30 índices, FK reales | `docs/schema_smart_gym.sql` |
| Sincronización | Supabase/PostgreSQL (réplica opcional), lotes 10–15, pull incremental | `docs/arquitectura/03-sincronizacion-smart-gym.md` |
| Biometría | Sidecar **C#/.NET** `SmartGym.Biometrics.exe` + DigitalPersona OneTouch SDK + U.are.U 4500 | `docs/investigacion-sidecar-biometrico.md` |
| Impresión | ESC/POS (CP850, ancho 32/42/48, logo ≤512 KB), portado de Ferre-POS | `docs/arquitectura/00-decisiones-tecnicas-smart-gym.md §6` |
| Tests | **122 tests unitarios** en 9 módulos Rust, todos documentados | `src-tauri/src/*.rs` (ver `03-checklist-comportamiento-esperado.md`) |
| Seguridad | Sesión revalidada en cada comando, permisos config-driven, reautorización con clave | `docs/arquitectura/04-seguridad-smart-gym.md` |

### Módulos funcionales (fase inicial)

1. Dashboard — 2. Miembros — 3. Membresías (catálogo de planes) — 4. Suscripciones — 5. Control de Acceso (biometría + Kiosco tablet) — 6. Inventario — 7. POS — 8. Finanzas/Caja — 9. Bitácora/Auditoría — 10. Seguridad/Permisos.

---

## 2. Mapa de las 4 categorías de rescate

La migración se divide en 4 categorías. Cada categoría corresponde a un documento de esta carpeta y define **qué se rescata, de dónde, y cómo se aterriza en .NET**:

| # | Categoría | Qué se rescata | Fuente | Documento |
|---|---|---|---|---|
| 1 | **Modelo de datos** | Las 30 tablas, FKs reales, 20 triggers `updated_at`, índices, seed mínimo (rol SUPERADMIN + Sede Principal). Portar como SQL literal, no rediseñar. | `docs/schema_smart_gym.sql` | `01-modelo-datos.md` |
| 2 | **Reglas de negocio** | Los 12 flujos transaccionales (alta socio, enrolamiento, venta membresía, congelamiento, cancelación, acceso Kiosco, venta POS, caja, impresión, cobranza, sync, backup) + los 14 principios no negociables. | `docs/arquitectura/00` y `05` | `02-reglas-de-negocio.md` |
| 3 | **Comportamiento esperado** | Los 122 tests de Rust convierten la suite de tests en el checklist de aceptación de la versión .NET. Un test Rust = un requisito verificable. | `src-tauri/src/*.rs` | `03-checklist-comportamiento-esperado.md` |
| 4 | **Hardware e integraciones** | El sidecar biométrico ya es C#/.NET (se reutiliza tal cual); hallazgos críticos del SDK DigitalPersona (ventana visible, WbioSrvc, DataPurpose) y subsistema ESC/POS. | `docs/investigacion-sidecar-biometrico.md` | `04-integracion-biometrica.md` |

**Categorías auxiliares (se rescatan como convención, no como código):**

| # | Categoría | Qué se rescata | Fuente | Documento |
|---|---|---|---|---|
| 5 | **Convenciones UI/UX** | Paleta, tipografía, glassmorphism, 19 componentes de negocio, feedback global, confirmaciones destructivas, convenciones de teclado. | `docs/arquitectura/07-diseno-ui-smart-gym.md` | `05-convenciones-ui-ux.md` |
| 6 | **Orden de implementación** | Secuencia por fases con dependencias entre módulos. | Síntesis de esta carpeta | `06-orden-de-implementacion-sugerido.md` |

---

## 3. Qué se conserva del proyecto y qué se descarta

**Se conserva (reutilizable en .NET):**
- El **sidecar biométrico** `SmartGym.Biometrics.exe` (ya es .NET Framework 4.8) — no se toca, se reutiliza como binario externo.
- La **lógica de negocio verificada** (traducida de Rust a C#).
- El **esquema SQLite** (SQL es portable tal cual).
- El **subsistema de impresión ESC/POS** como especificación de contenido.

**Se descarta (capa Tauri específica):**
- `#[tauri::command]`, `tauri-plugin-shell`, el patrón de invocación frontend→Rust.
- El patrón `spawn_blocking` de Tauri se sustituye por el modelo de tareas async de .NET.
- La capa React/MUI del frontend se sustituye por Blazor Hybrid (la apariencia se conserva vía convenciones, no vía código).

**Nota sobre `02-modelo-datos-smart-gym.md`:** existe como documento de arquitectura de referencia en `docs/arquitectura/`; el `01-modelo-datos.md` de esta carpeta contiene la copia literal ejecutable del schema.

---

## 4. Decisiones ya tomadas que la migración NO debe revertir

1. **Dinero siempre en enteros (centavos)** — nunca `float`/`double`/`decimal` sin razón documentada.
2. **El servidor (backend) calcula, el cliente solo envía intención** — precios y totales nunca vienen del frontend.
3. **Autorización siempre contra el hash almacenado** — revalidación en cada operación sensible.
4. **FK reales en SQLite** (`PRAGMA foreign_keys = ON` en cada conexión).
5. **4 tablas local-only excluidas del sync por lista de exclusión explícita**: `socios_biometricos`, `perifericos_config`, `sesiones`, `cuentas_recordadas_local`.
6. **Kiosco sin sesión** — subconjunto reducido de comandos, independiente del login administrativo.
7. **Sidecar biométrico con ventana visible permanente** (no depende de WbioSrvc).

---

## 5. Guía de navegación de esta carpeta

| Documento | Cuándo consultarlo |
|---|---|
| `01-modelo-datos.md` | Al crear la capa de base de datos en .NET (schema, EF/Dapper, migraciones) |
| `02-reglas-de-negocio.md` | Al implementar servicios de dominio y sus validaciones |
| `03-checklist-comportamiento-esperado.md` | Como suite de pruebas objetivo (xUnit) — un test Rust = un test .NET |
| `04-integracion-biometrica.md` | Al conectar el Kiosco y el enrolamiento con el sidecar |
| `05-convenciones-ui-ux.md` | Al construir cada pantalla Blazor |
| `06-orden-de-implementacion-sugerido.md` | Para planificar sprints y dependencias |
| `07-lecciones-de-proceso.md` | Regla de proceso para verificar integraciones con hardware real sin repetir los ciclos "funciona → no funciona" |

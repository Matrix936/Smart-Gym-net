# 06 — Orden de Implementación Sugerido (.NET MAUI + Blazor Hybrid)

**Base:** dependencias entre módulos derivadas del schema (`01-modelo-datos.md`) y los flujos (`02-reglas-de-negocio.md`). Cada fase indica su entregable, las dependencias y los tests del checklist (`03`) que debe dejar en verde.

**Principio rector:** la DB y el dominio van primero; la UI Blazor se construye cuando su backend de servicio ya tiene tests pasando. No empezar la UI de un módulo antes de tener su lógica verificada.

---

## Fase 0 — Fundación del proyecto

- Scaffold .NET MAUI + Blazor Hybrid (Windows como objetivo primario).
- Proyectos: `SmartGym.Core` (dominio + servicios), `SmartGym.Data` (acceso a SQLite), `SmartGym.App` (MAUI/Blazor), `SmartGym.Tests` (xUnit).
- Inyección de dependencias, configuración del app shell.
- Definición del tema visual base (ver `05-convenciones-ui-ux.md`): paleta, tipografía Hanken Grotesk offline, glassmorphism, dark/light.
- Entorno de pruebas reproducible desde el inicio: script de reset de DB local.

**Entregables:** solución compilando; shell de navegación con Topbar/Sidebar/Tema.

## Fase 1 — Capa de datos

- Ejecutar `01-modelo-datos.md` (el SQL literal) al crear la DB: 30 tablas, 20 triggers, índices, seed (`SUPERADMIN` + `Sede Principal`).
- `PRAGMA foreign_keys = ON` en cada conexión.
- Capa de acceso con Microsoft.Data.Sqlite; repositorios por dominio.
- Helpers: UUID v4, fechas ISO8601 UTC (`strftime('%Y-%m-%dT%H:%M:%fZ','now')`), transacciones.

**Entregables:** DB creada correctamente por el script; primeros tests de integración de la capa de datos.

**Criterio de salida:** `PRAGMA foreign_keys = ON` verificado; conteo de tablas = 30.

## Fase 2 — Seguridad base (auth + authorization + setup)

- `auth`: login (bcrypt, hash en `sesiones`, `expires_at`, logout con `revoked_at`), sin revelar si el email existe.
- `authorization`: catálogo de acciones en código, seed de `permisos_rol` para SUPERADMIN en primer arranque (idempotente), `requiere_permiso` + revalidación de sesión en cada operación sensible.
- `setup`: `completar_configuracion_inicial` (crea superadmin + datos fiscales + logo) — es el primer flujo que usa la app.
- Reautorización con clave (contra `password_hash`, nunca en memoria).

**Tests a portar (checklist `03`):** auth (13), authorization (5), setup (17) = 35 tests.

**Criterio de salida:** los 35 tests en verde; un usuario no autenticado no puede invocar ningún comando de negocio.

## Fase 3 — Socios (members)

- CRUD socios: crear (UUID, `estado='activo'`, `id_sede_registro`), buscar (nombre/email/teléfono), editar (preservando `id_sede` e `id`), cambiar estado (con historial en `socios_historial_estado`), soft delete.
- Resolución de `id_sede`: sesión local gana sobre el `id_sede` enviado por el cliente (ver tests `members.rs`).

**Tests a portar:** members (13).

## Fase 4 — Caja + Planes + Membresías

Dependencia: **caja antes que membresías** (la venta de membresía exige caja abierta).

- Catálogo `planes_membresia` (CRUD simple).
- `cash`: abrir/cerrar caja (monto esperado = movimientos `afecta_efectivo` + inicial), impedir doble apertura, corte con diferencia.
- `memberships`: vender (caja abierta, precio desde el plan, renovación sin perder días, pago parcial → `cuentas_cobrar`), congelar (respetando `dias_congelamiento_max`, extendiendo `fecha_fin`), cancelar (reautorización con clave).

**Tests a portar:** cash (12), memberships (14) = 26 tests.

**Criterio de salida:** 26 tests en verde; la venta de membresía queda en una transacción única (membresía + pago + movimiento de caja).

## Fase 5 — Control de acceso + Biometría (Kiosco)

- Integrar el sidecar `SmartGym.Biometrics.exe` (ver `04-integracion-biometrica.md`) — lanzar con `Process.Start`, verificar health endpoint, ventana visible, sin depender de WbioSrvc.
- Comandos de contexto Kiosco (sin sesión): identificar por huella, registrar acceso (`entrada`/`salida` alternando por día), evaluación de membresía (activa/congelada/vencida).
- Pantalla Kiosco: `KioskCard` centrado, resultado verde/rojo, sin detalles sensibles, error con reintento.
- Enrollment desde sesión administrativa; re-enrolar mismo dedo marca anterior `es_activa=0`.

**Tests a portar:** access (15), biometrics (20) = 35 tests.

## Fase 6 — Inventario + POS

Dependencia: caja abierta para vender.

- Catálogo `productos` + `inventario_sucursal` (stock, stock_minimo).
- `pos`: registrar venta (precio desde servidor, validar stock si `requiere_inventario`, descuento de stock, `ventas` + `detalle_ventas` + `caja_movimientos`), cancelar venta (reautorización con clave, restituye stock y movimiento).
- Lector de código de barras como listener de teclado (HID).

**Tests a portar:** pos (13).

## Fase 7 — Cobranza

- `registrar_abono`: `cobros_cuotas` (`exitoso`), resta saldo, `cobrada` al llegar a 0, `caja_movimientos` (requiere caja abierta).
- `cobros_recordatorios`: registro manual de envío (v1).

**Tests a portar:** ninguno dedicado hoy en Rust (la lógica se valida vía los tests de `vender_membresia_con_monto_menor_genera_cuenta_cobrar_con_saldo` y `registrar_abono`); agregar tests .NET para el flujo completo.

## Fase 8 — Auditoría transversal

- `bitacora_auditoria` en **todos** los comandos de escritura sensibles (patrón transversal, no un módulo aislado).
- Invariante estructural: todo comando de escritura registra auditoría + valida sesión y permiso.

**Tests:** invariantes estructurales del checklist `03` (sección final).

## Fase 9 — Impresión ESC/POS

- Portar la construcción del payload (CP850, ancho 32/42/48, densidad, logo ≤512 KB) siguiendo la especificación de Ferre-POS.
- Configuración de impresora en `perifericos_config` (local por terminal, no sincronizada).
- Impresión no bloqueante con opción de reimprimir.
- Tests de invariantes de payload (no finitos/excesivos) equivalentes a los 4 de Ferre-POS.

## Fase 10 — Sincronización con Supabase

- Worker con lotes de 10–15, reintento adaptativo (partir lote), timeout 8–12s, `catch`/manejo de errores sin tumbar el proceso.
- Pull incremental con cursor compuesto (`updated_at` + `id`), orden por dependencias FK.
- Conflicto: último escritor gana; los de impacto financiero se registran en auditoría, no se sobreescriben en silencio.
- **Exclusión explícita por nombre** de las 4 tablas local-only.
- Indicador visual de sync (verde/amarillo/rojo) + botón "Sincronizar ahora" + disparo al recuperar red.

## Fase 11 — Backup local

- Copia segura de SQLite (checkpoint/cierre consistente) con `socios_biometricos` incluida.
- Registro de resultado en log de archivo.

## Fase 12 — QA de regresión completo

- Ejecutar los 122 tests del checklist `03` como suite de aceptación.
- Checklist de regresión de negocio (caja, ventas, cancelaciones, acceso biométrico, inventario, sync offline/online dos terminales, permisos).
- Prueba real con el lector U.are.U 4500 y la impresora física.

---

## Diagrama de dependencias

```
Fase 0 (scaffold) → Fase 1 (DB)
                         │
                         ▼
Fase 2 (auth + setup) ──► Fase 3 (socios) ──► Fase 4 (caja + membresías)
                                                   │
                          ┌────────────────────────┘
                          ▼
Fase 5 (acceso + biometría/Kiosco)   Fase 6 (inventario + POS)
                          │                   │
                          ▼                   ▼
Fase 7 (cobranza)     Fase 8 (auditoría transversal)  Fase 9 (impresión)
                          │
                          ▼
Fase 10 (sync) → Fase 11 (backup) → Fase 12 (QA regresión)
```

Las fases 3–9 son independientes entre ramas y pueden paralelizarse tras la Fase 2, siempre respetando que **caja precede a membresías/POS** y que **auditoría es transversal**.

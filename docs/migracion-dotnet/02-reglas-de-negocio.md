# 02 — Reglas de Negocio (portables a .NET)

**Fuentes:** `docs/arquitectura/00-decisiones-tecnicas-smart-gym.md` y `docs/arquitectura/05-flujos-principales-smart-gym.md`.

Este documento congela las reglas de negocio que la versión .NET debe reproducir. Cada regla listada aquí tiene **cobertura en los tests de Rust** (ver `03-checklist-comportamiento-esperado.md`) — la suite es la verificación de que la regla se cumplió.

---

## 1. Principios no negociables (de `00`, §2)

1. **Dinero siempre en enteros (centavos).** Nunca `float`/`double` para montos.
2. **Nunca `.unwrap()`/`.expect()` en rutas críticas.** En .NET: sin `!` supresión de null en flujos de arranque, DB, venta/pago/acceso; manejo explícito de `Result`/excepciones.
3. **Autorización siempre contra el hash almacenado**, nunca comparación de valores en memoria/frontend. Revalidación server-side en cada operación sensible.
4. **El servidor calcula, el cliente solo envía intención.** El frontend nunca decide precio, total o descuento.
5. **Backend modular por dominio desde el día uno** (sin monolito).
6. **Sesión y permisos se revalidan contra la DB local en cada operación sensible.**
7. **Lotes de sincronización conservadores** (10–15 registros) con reintento adaptativo (partir el lote si falla).
8. **Config de periféricos local por terminal**, no sincronizada.
9. **Cleanup garantizado de listeners/polling de hardware** (huella, código de barras) al desmontar componentes.
10. **Lazy loading de librerías pesadas** (exportación a Excel/PDF, gráficas).
11. **Entorno de pruebas reproducible desde el inicio** (reset de DB + checklist de QA de regresión).
12. **Ningún trabajo bloqueante en el hilo principal** — DB, HTTP y file I/O en `Task.Run`/async correcto; polling con cancelación explícita al desmontar.
13. **La DB SQLite en uso nunca se cifra/borra/copia de forma destructiva con conexiones abiertas** — backup tras `PRAGMA wal_checkpoint` o cierre consistente.
14. **Metodología de depuración: auditar el patrón completo, no parchar el síntoma.** Toda corrección de un patrón se revisa contra todos los comandos que comparten ese patrón.

---

## 2. Stack de referencia (qué reemplaza qué)

| Capa | Rust (actual) | .NET (destino) |
|---|---|---|
| Backend lógico | Módulos Rust por dominio | Servicios de dominio C# (Dependency Injection) |
| DB | `rusqlite` sin ORM | Microsoft.Data.Sqlite (mismo schema; ver `01-modelo-datos.md`) |
| Autenticación | `bcrypt::verify`, sesiones `token_hash` | `BCrypt.Net-Next` (misma librería bcrypt) + tabla `sesiones` |
| Fechas | `chrono` | `DateTimeOffset` UTC, formato ISO8601 |
| HTTP | `reqwest` | `HttpClient` |
| Sidecar biométrico | `SmartGym.Biometrics.exe` (C#/.NET) | **Se reutiliza sin cambios** |
| Impresión | `printing.rs` (ESC/POS) | Reescribir el payload ESC/POS en C# siguiendo la misma especificación |
| App shell | Tauri v1 | .NET MAUI + Blazor Hybrid |

---

## 3. Catálogo de acciones y permisos (de `04-seguridad-smart-gym.md §3`)

Acciones usadas por los comandos (constantes en `authorization.rs`, seed para `SUPERADMIN` al primer arranque):

- `socios.crear`, `socios.editar`, `socios.eliminar`
- `membresias.crear`, `membresias.congelar`, `membresias.cancelar`
- `pos.vender`, `pos.cancelar_venta`
- `caja.abrir`, `caja.cerrar`
- `cobranza.registrar_abono`
- `configuracion.editar_perifericos`, `configuracion.gestionar_usuarios`
- `acceso.ver_bitacora`, `acceso.forzar_entrada_manual`

Reglas de seguridad asociadas:
- **Login** nunca revela si el email existe (mensaje genérico "credenciales inválidas").
- **Sesión**: token hasheado en `sesiones`, `expires_at` (sugerido 12h), `revoked_at` en logout. Revalidación en cada comando sensible.
- **Reautorización con clave** (reingreso de contraseña) para operaciones extra-sensibles: cancelar membresía activa, cancelar venta. Siempre validada contra `password_hash`, nunca en memoria. (Corrige hallazgo P0 de Ferre-POS.)
- **Contraseña mínima: 8 caracteres**, validada en backend.
- **Errores saneados**: los errores SQLite/IO se transforman a variantes de negocio; el detalle técnico se loguea localmente pero nunca se serializa al frontend.
- **Modo Kiosco**: sin `validar_sesion` (no hay sesión), pero con validación de contexto (flag interno de modo Kiosco). Solo expone: identificar socio por huella y registrar acceso. Nunca datos de membresías/finanzas.

---

## 4. Flujos transaccionales (de `05`, v1)

Todos corren dentro de una **transacción SQLite** — o todos los pasos, o ninguno.

### 4.1 Alta de socio
- Insertar en `socios` (`estado='activo'`, `id_sede_registro`), registrar `bitacora_auditoria` (`accion='socio.creado'`).
- Validaciones: nombre obligatorio; email opcional con formato válido (sin unicidad forzada).
- **El alta NO exige membresía activa en el mismo paso** — son operaciones separadas.

### 4.2 Enrolamiento biométrico
- Solo desde sesión administrativa, **nunca** desde el contexto Kiosco.
- Insertar en `socios_biometricos` (`es_activa=1`). Un socio puede tener varias huellas; re-enrolar el mismo dedo marca la anterior `es_activa=0` (trazabilidad, no borrado).

### 4.3 Venta de membresía (alta o renovación)
- Requiere **caja abierta** (`cajas_sesiones.estado='abierta'`) para el usuario/sede — si no, se rechaza.
- `precio_centavos` se calcula desde `planes_membresia`, nunca desde el frontend.
- `fecha_fin` según `dias_vigencia`; en renovación de membresía aún vigente, `fecha_inicio` = `fecha_fin` anterior (no se pierden días).
- Inserta: `membresias` (`estado='activa'`), `membresias_pagos`, `caja_movimientos` (`tipo='ingreso'`, `referencia_tipo='pago_membresia'`).
- Si `monto_recibido_centavos < precio_centavos` → genera fila en `cuentas_cobrar` con el saldo.
- `monto_recibido_centavos` no puede ser negativo ni exceder el precio sin autorización explícita.
- Impresión de recibo (si impresora configurada), no bloqueante.

### 4.4 Congelamiento de membresía
- `(fecha_fin - fecha_inicio)` no puede exceder `dias_congelamiento_max` del plan.
- Inserta `membresias_congelamientos`; actualiza `membresias.estado='congelada'` si aplica; **extiende `fecha_fin` por los días congelados**.

### 4.5 Cancelación de membresía
- **Requiere reautorización con clave** (operación extra-sensible).
- Actualiza `membresias.estado='cancelada'`, `fecha_cancelacion=now()`.
- **No hay reembolso automático** — la devolución es un `caja_movimientos` tipo `egreso` explícito y separado, con su propia autorización.

### 4.6 Control de acceso (Kiosco)
- Identifica socio por huella (matching 1:N vía sidecar).
- Sin sesión administrativa. Evalúa la membresía:
  - Sin membresía activa → `denegado` / `membresia_vencida`.
  - Membresía `congelada` → `denegado` / `membresia_congelada` (**el congelamiento pausa el derecho de acceso**).
  - Membresía `activa` → `concedido`; `tipo` = `entrada`/`salida` alternando respecto al último registro del día para ese socio.
- Actualiza `socios.fecha_ultimo_acceso` solo si fue concedido.
- Pantalla Kiosco: verde (bienvenida) / rojo (motivo de rechazo sin detalles sensibles).
- Si el dispositivo falla, mostrar error claro con opción de reintentar — nunca colgarse.

### 4.7 Venta POS
- Requiere caja abierta. Precio tomado de `productos.precio_venta_centavos` en servidor (nunca del frontend).
- Valida stock en `inventario_sucursal` si `requiere_inventario=1`; descuenta stock.
- Inserta `ventas` + `detalle_ventas` + `caja_movimientos` (`referencia_tipo='venta'`).
- **Cancelación de venta**: requiere reautorización con clave; restituye stock y movimiento de caja.

### 4.8 Apertura y cierre de caja
- Apertura: verifica que no haya sesión `abierta` para ese usuario/sede; inserta `cajas_sesiones`.
- Cierre: `monto_esperado_centavos` = suma de `caja_movimientos` con `afecta_efectivo=1` + `monto_inicial_centavos`; guarda `monto_final_centavos` (contado físico) y la diferencia; `estado='cerrada'`, `fecha_cierre=now()`.

### 4.9 Impresión de comprobantes
- ESC/POS: ancho/densidad/logo según `perifericos_config`.
- **No bloqueante**: si falla la impresión, la transacción de negocio ya se completó. Botón "reimprimir" desde historial.

### 4.10 Cobranza
- `cuentas_cobrar` se genera en venta de membresía con pago parcial.
- `registrar_abono`: inserta `cobros_cuotas` (`resultado='exitoso'`), resta `saldo_pendiente_centavos`; si llega a 0 → `estado='cobrada'`; inserta `caja_movimientos` (requiere caja abierta).
- Recordatorios: en v1 solo se registra el envío (`cobros_recordatorios`), no se automatiza.

### 4.11 Sincronización (resumen)
- Cada insert/update en tabla sincronizable queda con `sincronizado=0`; se sube en el siguiente push.
- Las 4 tablas local-only (`socios_biometricos`, `perifericos_config`, `sesiones`, `cuentas_recordadas_local`) **nunca** entran al worker — exclusión por nombre explícito en código.

### 4.12 Backup local
- Copia cifrada del SQLite completo a respaldo local (y opcionalmente nube).
- Incluye `socios_biometricos` — **es su única red de seguridad** (no se sincroniza).
- El resultado se registra en log de archivo, no en `bitacora_auditoria` (evita ruido).

---

## 5. Tablas por flujo (referencia rápida)

| Flujo | Tablas principales |
|---|---|
| Alta de socio | `socios`, `bitacora_auditoria` |
| Enrolamiento | `socios_biometricos`, `bitacora_auditoria` |
| Venta de membresía | `membresias`, `membresias_pagos`, `caja_movimientos`, `cuentas_cobrar` (si aplica), `bitacora_auditoria` |
| Congelamiento | `membresias_congelamientos`, `membresias`, `bitacora_auditoria` |
| Cancelación | `membresias`, `bitacora_auditoria` |
| Acceso (Kiosco) | `accesos_bitacora`, `socios` (fecha_ultimo_acceso) |
| Venta POS | `ventas`, `detalle_ventas`, `inventario_sucursal`, `caja_movimientos`, `bitacora_auditoria` |
| Caja | `cajas_sesiones`, `bitacora_auditoria` |
| Cobranza | `cobros_cuotas`, `cuentas_cobrar`, `caja_movimientos`, `cobros_recordatorios` |

---

## 6. Nombres de comandos Tauri actuales (equivalente a métodos del servicio)

Estos nombres son la referencia de contrato de la API. En .NET se convierten en métodos de servicio con el mismo nombre/contrato:

| Comando Tauri | Acción/permiso | Módulo |
|---|---|---|
| `crear_socio(token, datos)` | `socios.crear` | members |
| `registrar_huella(token, id_socio, dedo, ruta_template)` | `socios.editar` | biometrics |
| `vender_membresia(token, id_socio, id_plan, metodo_pago, monto_recibido_centavos)` | `membresias.crear` | memberships |
| `congelar_membresia(token, id_membresia, fecha_inicio, fecha_fin, motivo)` | `membresias.congelar` | memberships |
| `cancelar_membresia(token, id_membresia, motivo)` | `membresias.cancelar` + reautorización | memberships |
| `registrar_acceso(id_socio_identificado \| None)` | contexto Kiosco | access |
| `registrar_venta(token, id_socio_opcional, items[], metodo_pago)` | `pos.vender` | pos |
| `abrir_caja(token, monto_inicial_centavos)` | `caja.abrir` | cash |
| `cerrar_caja(token, id_sesion, monto_final_contado_centavos)` | `caja.cerrar` | cash |
| `registrar_abono(token, id_cuenta, monto_centavos, metodo_pago)` | `cobranza.registrar_abono` | finance |
| `login(email, password)` / `logout(token)` | — | auth |
| `completar_configuracion_inicial(...)` | — | setup |

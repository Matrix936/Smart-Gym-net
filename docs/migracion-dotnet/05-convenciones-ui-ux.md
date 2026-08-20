# 05 — Convenciones UI/UX (aplicables a Blazor)

**Fuente:** `docs/arquitectura/07-diseno-ui-smart-gym.md` (diseño v1, React + MUI 6 + Vite). Este documento congelas las convenciones **de apariencia y comportamiento** para que la versión .NET MAUI + Blazor Hybrid reproduzca la misma experiencia. Se indican equivalentes sugeridos en Blazor; la apariencia es la fuente de verdad, no el framework.

---

## 1. Fundamentos visuales

### 1.1 Paleta de color (cyan/turquesa, glassmorphism)

| Elemento | Dark | Light |
|---|---|---|
| Primary | `#00d2ff` (cyan brillante) | `#0097b2` (turquesa profundo) |
| Secondary | `#7c4dff` (violeta) | `#7c4dff` (violeta) |
| Background default | `#10141a` | `#f8f9fa` |
| Background paper | `rgba(255,255,255,0.1)` | `#ffffff` |
| Text primary | `#ffffff` | hereda del tema |
| Divider | `rgba(255,255,255,0.15)` | hereda del tema |
| Error | `#ff5252` | hereda del tema |
| Warning | `#ffab40` | hereda del tema |
| Success | `#69f0ae` | hereda del tema |

### 1.2 Tipografía

**Hanken Grotesk** (400, 500, 600, 700) — geometría moderna, limpia, profesional. Familia fallback: `"Hanken Grotesk", "Roboto", "Helvetica", "Arial", sans-serif`. Pesos: `h4`/`h5` = 700, `h6` = 600, `subtitle1`/`subtitle2` = 600. En .NET MAUI empaquetar la fuente como recurso de la app (offline, sin CDN).

### 1.3 Iconografía

Set consistente tipo `*Outlined` (trazo fino). En Blazor: un solo set de íconos (MudBlazor Icons o MAUI FontIcon), siempre el mismo en toda la app.

### 1.4 Base de componentes

Componentes base del framework (botón, campo de texto, card, drawer, appbar, tabla) personalizados vía tema global: `borderRadius` pill, glassmorphism, colores por modo. **Una sola definición de tema** compartida por dark/light.

---

## 2. Componentes de negocio (equivalencia Blazor)

| # | Componente (v1 React) | Función | Equivalente sugerido Blazor |
|---|---|---|---|
| 1 | `DataTable` | Listados: socios, membresías, ventas, bitácora | `MudDataGrid` / `QuickGrid` |
| 2 | `AsyncButton` | Botón con spinner — evita doble-click | Componente propio `AsyncButton` |
| 3 | `ConfirmDialog` | Confirmación destructiva | Componente propio `ConfirmDialog` |
| 4 | `TablePagination` | Paginación en listados | `MudPagination` / propio |
| 5 | `EmptyState` | Estado vacío ilustrado | Componente propio |
| 6 | `FeedbackSnackbar` | Notificaciones globales (1 sola instancia) | `MudSnackbar` / componente propio global |
| 7 | `SearchInput` | Búsqueda con debounce (~300ms) | `Debounce` con `MudTextField` |
| 8 | `StatusBadge` | Pastilla de estado con color semántico | `MudChip` |
| 9 | `MoneyDisplay` | Formatea centavos → moneda | Componente propio (fuente: enteros `_centavos`) |
| 10 | `FormField` | Wrapper de campo con error `helperText` | Componente propio |
| 11 | `Modal` | Contenedor para formularios | `MudDialog` |
| 12 | `AuthCard` | Glassmorphism + spotlight para Login/Setup | Componente propio |
| 13 | `ThemeToggle` | Botón flotante dark/light | Componente propio |
| 14 | `Topbar` | Barra superior persistente | Layout propio |
| 15 | `Sidebar` | Navegación colapsable (filtrar por permisos) | Layout propio / `MudNavMenu` |
| 16 | `SyncIndicator` | Estado de sincronización (verde/amarillo/rojo) | Componente propio |
| 17 | `PermissionGate` | Visibilidad por permisos | Componente propio (`AuthorizeView`-style pero por acción config-driven) |
| 18 | `KioskCard` | Resultado del Kiosco (tablet) | Componente propio |
| 19 | `DashboardMetricCard` | Métricas del Dashboard | Componente propio |

---

## 3. Layout general

- **Topbar** persistente en pantallas administrativas: indicador de sync, WiFi, reloj, notificaciones, menú de usuario. Glassmorphism.
- **Sidebar** colapsable, navegación entre módulos; filtra por `permisos_rol`.
- **Modo Kiosco**: pantalla completa, sin Topbar ni Sidebar, un solo `KioskCard` centrado.

---

## 4. Glassmorphism (regla de implementación)

- Aplicado a Card, Paper, AppBar, Drawer via `backdrop-filter: blur()` + fondos translúcidos + bordes sutiles.
- **Dark:** `rgba(255,255,255,0.1)` + blur 20px + border `1px solid rgba(255,255,255,0.2)` + radius 24 + sombra `0 8px 32px rgba(0,0,0,0.3)`.
- **Light (AuthCard):** `rgba(255,255,255,0.6)` + blur 40px + border `1px solid #ffffff` + radius 16 + sombra suave.
- **Siempre** incluir el prefijo WebKit (`-webkit-backdrop-filter`) junto a `backdrop-filter` para Safari/iOS.

---

## 5. Patrones de formularios

- `FormField` (pill shape, radius 30) en todos los formularios.
- `AsyncButton` en todo submit — deshabilitado durante la operación.
- Errores vía feedback global (nunca `useState`/estado local para errores).
- Validación de UX en el cliente + validación real en backend (autoridad).

## 6. Patrones de tablas

- `DataTable` + paginación; `SearchInput` con debounce ~300ms; `StatusBadge` para columnas de estado.

## 7. Patrones de diálogos

- `ConfirmDialog` para acciones destructivas.
- Variante con reingreso de contraseña para operaciones sensibles (cancelar membresía, cancelar venta).

---

## 8. Convención de imágenes (logo, fotos)

**Regla fija:** toda imagen (logo en Topbar, recibos, SetupWizard) usa `object-fit: contain` dentro de un contenedor de tamaño fijo con `overflow: hidden`. **Nunca `cover`** — el logo no debe recortarse. Equivalente CSS en Blazor: `object-fit: contain; width:100%; height:100%` en un contenedor con dimensiones fijas y `overflow:hidden`.

---

## 9. Dark/Light Mode

- Toggle flotante inferior-derecha.
- Preferencia persistente localmente (en v1 React: `localStorage`; en MAUI: `Preferences` del dispositivo).
- El tema se aplica vía un único proveedor global de tema compartido.

---

## 10. Sistema de Feedback

- API global: `showSuccess`, `showError`, `showWarning`, `showInfo`.
- **Una sola instancia** de snackbar global en el árbol.
- **Nunca** usar alertas locales ad-hoc — siempre el canal global.

---

## 11. Convenciones de teclado

| Regla | Implementación |
|---|---|
| **Enter = submit** | El formulario envuelve sus campos en `<form onSubmit>`; botón principal `type="submit"`. Sin `onClick` redundante. |
| **Escape = cerrar** | Diálogos responden a Escape. Nunca deshabilitar `Escape` sin justificación. |
| **Tab = siguiente campo** | Orden visual del DOM; sin `tabIndex` manual salvo casos especiales. |
| **Botones no-submit** | Cancelar/Anterior son `type="button"` explícito; nunca disparan submit accidental. |
| **AsyncButton en form** | `type="submit"`, sin `onClick`; el `onSubmit` maneja la lógica async. |

---

## 12. Confirmaciones — cuándo sí y cuándo no

Regla general: **si la acción tiene efectos secundarios irreversibles → ConfirmDialog. Si solo inserta/edita con formulario validado → no.**

| Acción | ¿Requiere confirmación? |
|---|---|
| Crear registro nuevo | No |
| Editar registro existente | No |
| Soft delete / desactivar registro | **Sí** |
| Cerrar caja | **Sí** (monto esperado se congela) |
| Vender membresía | No |
| Cancelar membresía activa | **Sí** (pierde días restantes, requiere contraseña) |
| Registrar venta POS | No |
| Cancelar venta | **Sí** (restituye stock, requiere contraseña) |
| Configuración inicial | **Sí** (crea superadmin, no se deshace) |
| Abrir caja | No |
| Conceder/denegar acceso manual | No |
| Enrollment biométrico | No |
| Desactivar template biométrico | **Sí** |

**Convenciones del diálogo de confirmación:**
- Escape cancela; **Enter confirma solo si el foco está en "Confirmar"** (`autoFocus`), nunca en todo el diálogo (evita confirmaciones accidentales).
- "Cancelar" se deshabilita durante la carga.
- "Confirmar" muestra spinner + texto de carga.
- **Una sola instancia** por componente, al final del JSX.

---

## 13. Contrato de invocación backend (lección Tauri — no repetir)

La versión React/Rust documentó un bug real de naming en la capa de serialización (Tauri convertía `snake_case` → `camelCase` solo en el primer nivel). **En .NET esto desaparece por diseño** (los servicios se invocan como métodos tipados), pero la lección de fondo se conserva: **los nombres de los parámetros de la API deben ser un contrato único y verificado por tests** — en .NET, tipar inputs con DTOs y validarlos en el servicio, nunca dependiendo de convenciones implícitas de serialización.

## 14. Estructura de carpetas (referencia del frontend React)

La estructura `components/ui/`, `contexts/`, `hooks/`, `pages/`, `lib/`, `theme.ts` se traduce en Blazor a: `Shared/` (componentes), `Layout/` (MainLayout, KioskLayout), `Pages/`, `Services/` (DI), `wwwroot/css` (tokens del tema).

---

## Nota

El diseño v1 dejó como pendiente implementar los componentes 1–9, 11, 14–19 de la tabla §2 conforme se desarrolle cada módulo. En .NET aplica lo mismo: se implementan cuando el módulo que los usa entre en desarrollo.

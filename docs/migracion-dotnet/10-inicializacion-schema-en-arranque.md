# 10 - Inicialización del schema en cada arranque

**Fecha:** 2026-08-22. **Alcance:** `SmartGym.Data/Db/DbInitializer.cs` + `Scripts/schema_smart_gym.sql`. Decisión de comportamiento de arranque para toda la aplicación, motivada por el módulo de Maquinaria: sin mecanismo de migraciones, ejecutar el script idempotente en cada arranque era la única vía para llevar tablas nuevas a bases de datos ya creadas.

---

## La decisión

Antes: el script solo se ejecutaba si la base estaba **vacía** (early-return en `DbInitializer.Initialize`). Consecuencia: cualquier tabla agregada al `.sql` después del primer arranque **nunca llegaba a las bases existentes** — caso concreto: `maquinaria` no se habría creado en la BD de producción.

Ahora: `Initialize()` ejecuta el script completo **en cada arranque**. Es seguro por diseño del script (verificado exhaustivamente, ver garantías) y cuesta milisegundos de DDL `IF NOT EXISTS`.

## Garantías de seguridad (verificadas, no asumidas)

Auditoría línea por línea del script buscando sentencias destructivas o no-idempotentes:

| Tipo | Contenido real del script |
|---|---|
| `CREATE TABLE/INDEX/TRIGGER` | Todos con `IF NOT EXISTS` — sobre objetos existentes SQLite los ignora; **las filas no se tocan** |
| Seeds (`INSERT`) | Solo 2, ambos idempotentes: roles con `INSERT OR IGNORE`, sede principal con `WHERE NOT EXISTS` |
| `DROP / DELETE / TRUNCATE / UPDATE / ALTER` | **Cero** en todo el script |

Repetir el script sobre una base con datos reales no sobrescribe, trunca ni borra nada — es estructuralmente imposible por cómo está escrito. Verificaciones:

- Test `Schema_es_idempotente` (`Data/DbSchemaTests.cs`): ejecuta `Initialize` dos veces sobre el mismo fixture y comprueba que el seed no duplica.
- Probado sobre la BD real de desarrollo (con meses de datos): `Initialize` + consultas posteriores, filas intactas.

## Limitación explícita: esto NO migra columnas

`CREATE TABLE IF NOT EXISTS` solo crea tablas que **no existen**. Si una tabla ya existe, la sentencia se ignora completa — incluidos cambios de definición:

- ✅ Tabla nueva completa (como `maquinaria`) → llega a todas las BDs al arrancar.
- ❌ Columna nueva/renombrada/cambiada de tipo en una tabla existente → **NO se aplica** a bases ya creadas; el `.sql` queda desfasado y las queries con esa columna fallarán con "no such column".
- El script no contiene `ALTER TABLE` y no debe contenerlo: un `ALTER` plano fallaría cuando la columna ya exista.

## Pendiente: implementar migraciones reales

**Gatillo concreto:** la primera vez que se necesite modificar una columna de una tabla que ya tiene commits en producción (agregar/quitar/renombrar/cambiar tipo), este mecanismo de `CREATE TABLE IF NOT EXISTS` deja de ser suficiente.

En ese momento, implementar un sistema real de migraciones versionadas usando la tabla **`schema_migrations`** que ya existe en el schema desde Fase 1 (vacía y sin usar): cada migración con su versión registrada, ejecutar solo las pendientes en cada arranque. **No** intentar un `ALTER TABLE` ad-hoc dentro del script actual — fallaría en las bases que ya tienen la columna.

## Incidentes del patrón (columna nueva en tabla existente)

Ambos casos con el mismo mecanismo exacto: un commit agrega una columna al `.sql`, la BD real no la tiene (el script no migra columnas), y la primera query que referencia esa columna falla con "no such column".

| # | Columna | Commit | Síntoma visible | Detección |
|---|---|---|---|---|
| 1 | `cuentas_cobrar.origen` (+ `id_membresia` nullable) | `50c7a0f` (crédito POS) | "No se pudo cargar la cobranza" en /cobranza — error tragado por el catch genérico de la página | Reproducido con query directa sobre la BD real; resuelto con ALTER puntual + backup |
| 2 | `cuentas_cobrar.id_venta` (+ índice) | `0f19d8d` (cancelación de crédito) | **Crash silencioso de toda la app al arrancar**: `dotnet run` vuelve al prompt sin salida; en el Visor de Eventos aparece Evento 1000 / `0xc000027B` stowed exception en `Microsoft.UI.Xaml.dll` (3 intentos, 3 crashes) | `DbInitializer` ejecuta el script completo al arranque; el `CREATE INDEX idx_cuentas_cobrar_id_venta` falla contra la tabla vieja y la excepción mata el proceso. Reproducido lanzando el exe directamente (exit `-1073741189`) y probando el statement sobre una copia de la BD. Resuelto con backup + `ALTER TABLE cuentas_cobrar ADD COLUMN id_venta TEXT REFERENCES ventas(id_venta)` + crear el índice |

Nota sobre el caso 2: el crash es **silencioso por diseño de WinUI** — las stowed exceptions no escriben nada en consola, así que `dotnet run` parece "terminar bien". La evidencia solo existe en el Visor de Eventos (Application, Evento 1000).

### Nota de proceso

Antes de commitear cualquier cambio de schema que agregue columnas a una tabla existente, correr la app localmente contra una base de datos real (no solo la suite de tests) para confirmar que el arranque no falla — la suite de tests **no detecta este tipo de error** porque usa bases de datos frescas en cada test (el `CREATE TABLE` nuevo sí incluye las columnas).

---

## Caso 3 (producción real, 2026-08-25): promociones.id_plan

**Primer incidente con datos reales de un cliente.** Commit `03e9a7c` (combo_membresia) agregó `promociones.id_plan` + índice; la BD de producción no tenía la columna → mismo crash silencioso al arrancar (0xc000027B en Visor de Eventos, 3 intentos). Confirmado leyendo la BD directamente con Microsoft.Data.Sqlite.

**Resuelto en dos capas (commit `f5c70c0`):**

1. **DbInitializer resiliente (defensa genérica):**
   - Pre-chequeo: todo `CREATE INDEX` del script se valida contra `PRAGMA table_info` de la BD destino; si falta alguna columna, el índice se omite con log (nunca tumba el arranque).
   - Fallback sentencia-por-sentencia: si el batch completo falla por cualquier otro motivo, se aplica individualmente lo que se pueda, registrando cada fallo vía `DbInitializer.LogWarning` (cableado a `sg_diag_render.log`).
2. **Migración puntual:** `docs/migracion-dotnet/scripts/migrar-produccion-combo-membresia.ps1` — backup automático + ALTER condicional + verificación de filas intactas.

## Decisión: migraciones versionadas NO se construyen por ahora

Con producción activa, el gatillo documentado arriba quedó técnicamente activado. Sin embargo, tras el caso 3 se decide **no construir aún** el sistema completo de migraciones versionadas (`schema_migrations`), porque:

- El hardening del inicializador es una **defensa genérica**: ante cualquier delta futuro (columna/índice nuevo sobre tabla existente), el arranque ya no crashea — omite lo que no puede aplicar y lo registra.
- Los deltas hasta hoy son solo "columna nueva nullable + índice", aplicables con scripts puntuales como este.
- Un sistema versionado completo es inversión considerable que se justificará cuando haga falta algo que los scripts puntuales no cubren bien: **migrar datos** (transformar valores, partir columnas, backfills), no solo agregar estructura aditiva nullable.

**Re-gatillo para reconsiderarlo:** la primera vez que un cambio requiera transformar o mover datos existentes (no solo estructura aditiva nullable).## Referencia cruzada

Consideración análoga de rendimiento: la función SQL `sin_acentos()` (búsqueda insensible a acentos, ver commit `1cf9dd0`) aplicada dentro de un `LIKE` impide el uso de índices sobre la columna (no sargable). Aceptable para catálogos de bajo volumen; no replicarla en tablas de alto volumen (`bitacora_auditoria`, `ventas`) sin una columna normalizada persistida.

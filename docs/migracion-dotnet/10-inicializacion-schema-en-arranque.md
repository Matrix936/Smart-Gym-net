# 10 - Inicialización del schema en cada arranque

**Fecha:** 2026-08-22 (original), 2026-08-26 (auto-columnas). **Alcance:** `SmartGym.Data/Db/DbInitializer.cs` + `Scripts/schema_smart_gym.sql`. Decisión de comportamiento de arranque para toda la aplicación, motivada por el módulo de Maquinaria: sin mecanismo de migraciones, ejecutar el script idempotente en cada arranque era la única vía para llevar tablas nuevas a bases de datos ya creadas.

---

## La decisión

Antes: el script solo se ejecutaba si la base estaba **vacía** (early-return en `DbInitializer.Initialize`). Consecuencia: cualquier tabla agregada al `.sql` después del primer arranque **nunca llegaba a las bases existentes** — caso concreto: `maquinaria` no se habría creado en la BD de producción.

Ahora: `Initialize()` ejecuta el script completo **en cada arranque**. Es seguro por diseño del script (verificado exhaustivamente, ver garantías) y cuesta milisegundos de DDL `IF NOT EXISTS`.

## Detección automática de columnas faltantes (2026-08-26)

### Problema resuelto

Antes de este cambio, una columna nueva agregada al `.sql` sobre una tabla existente **nunca llegaba** a bases de datos ya creadas. Cada vez que ocurría, requería un script manual de migración (`.ps1`) con backup + ALTER + verificación. Los casos 1-3 en producción demostraron que este flujo manual es frágil y depende de que alguien recuerde correr el script.

### Solución

Un paso previo dentro de `DbInitializer.Initialize` que se ejecuta **antes** del script idempotente:

```
Initialize(dbPath, script)
  │
  ├─① AgregarColumnasFaltantes(dbPath, script)    ← NUEVO
  │     ├─ Parsear schema esperado del .sql embebido
  │     ├─ Comparar con PRAGMA table_info de cada tabla existente
  │     ├─ Si hay faltantes:
  │     │   ├─ Backup automático (.db + .wal + .shm) con timestamp
  │     │   ├─ Si backup falla → ABORTAR (no tocar schema)
  │     │   ├─ ALTER TABLE ADD COLUMN por cada columna faltante
  │     │   └─ Loggear cada adición en sg_diag_render.log
  │     └─ Si no hay faltantes → noop
  │
  ├─② OmitirIndicesNoEjecutables(script, conn)   ← EXISTENTE (red de seguridad)
  │
  └─③ EjecutarBatch / EjecutarPorSentencia       ← EXISTENTE
```

### Reglas de seguridad

| Regla | Comportamiento |
|-------|---------------|
| Backup obligatorio | Se crea `.db.bak_{timestamp}` (+ sidecars) antes de cualquier ALTER. Si falla (disco lleno, permisos), se aborta sin tocar nada. |
| Solo nullable o con DEFAULT | Columnas `NOT NULL` sin `DEFAULT` se omiten con log — requieren backfill manual. |
| Cada ALTER en su propia conexión | Si uno falla, se loggea y se continúa con el siguiente. |
| Backups se auto-limpian | Se mantienen solo los últimos 5; los más antiguos se borran. |
| Log con rotación | `sg_diag_render.log` rota a `.old` al superar 5 MB. |
| Omitir índices se mantiene | La protección existente de `OmitirIndicesNoEjecutables` sigue activa como red de seguridad. |

### Cableado del log de diagnóstico

`MauiProgram.cs` cablea `DbInitializer.LogWarning` a `sg_diag_render.log` en `%APPDATA%\Smart-Gym-net\`. Esto también habilita el logging de omisiones de índices (antes silencioso en producción).

### Archivos modificados

| Archivo | Cambio |
|---------|--------|
| `SmartGym.Data/Db/DbInitializer.cs` | `ParsearSchemaEsperado`, `DetectarColumnasFaltantes`, `CrearBackup`, `LimpiarBackupsAntiguos`, `RotarLogSiNecesario`, `AgregarColumnas`, `AgregarColumnasFaltantes` |
| `SmartGym.App/MauiProgram.cs` | Cablear `DbInitializer.LogWarning` + `RotarLogSiNecesario` |
| `SmartGym.Tests/Data/DbInitializerAutoColumnsTests.cs` | 3 tests: auto-agrega, backup-fallo, no-duplica |
| `SmartGym.Tests/Data/DbInitializerLegacyTests.cs` | Tests actualizados para reflejar que las columnas ahora se agregan automáticamente |

### Tests

- `auto_columnas_agrega_columna_faltante_con_backup`: BD legacy sin `id_plan` → columna agregada + backup creado + fila intacta + log registrado
- `auto_columnas_backup_fallo_no_toca_schema`: Si el backup falla, no se toca el schema
- `auto_columnas_no_duplica_si_ya_existe`: BD completa → sin backup ni ALTERs

## Garantías de seguridad (verificadas, no asumidas)

Auditoría línea por línea del script buscando sentencias destructivas o no-idempotentes:

| Tipo | Contenido real del script |
|---|---|
| `CREATE TABLE/INDEX/TRIGGER` | Todos con `IF NOT EXISTS` — sobre objetos existentes SQLite los ignora; **las filas no se tocan** |
| Seeds (`INSERT`) | Solo 2, ambos idempotentes: roles con `INSERT OR IGNORE`, sede principal con `WHERE NOT EXISTS` |
| `DROP / DELETE / TRUNCATE / UPDATE / ALTER` | **Cero** en todo el script (el ALTER lo ejecuta `AgregarColumnasFaltantes`, no el script) |

Repetir el script sobre una base con datos reales no sobrescribe, trunca ni borra nada — es estructuralmente imposible por cómo está escrito. Verificaciones:

- Test `Schema_es_idempotente` (`Data/DbSchemaTests.cs`): ejecuta `Initialize` dos veces sobre el mismo fixture y comprueba que el seed no duplica.
- Probado sobre la BD real de desarrollo (con meses de datos): `Initialize` + consultas posteriores, filas intactas.

## Limitación restante: columnas NOT NULL sin DEFAULT

El mecanismo automático cubre columnas nullable y con DEFAULT. Columnas `NOT NULL` sin `DEFAULT` sobre tablas con filas existentes se omiten con log — el `ALTER TABLE` fallaría porque SQLite no puede asignar un valor a las filas existentes. Estos casos requieren:

1. Un script manual de migración con backfill de datos, o
2. Agregar un `DEFAULT` temporal en el schema SQL (luego quitarlo en una migración posterior).

## Incidentes del patrón (columna nueva en tabla existente)

Ambos casos con el mismo mecanismo exacto: un commit agrega una columna al `.sql`, la BD real no la tiene (el script no migra columnas), y la primera query que referencia esa columna falla con "no such column".

| # | Columna | Commit | Síntoma visible | Resolución original |
|---|---|---|---|---|
| 1 | `cuentas_cobrar.origen` (+ `id_membresia` nullable) | `50c7a0f` (crédito POS) | "No se pudo cargar la cobranza" en /cobranza | Script manual `.ps1` |
| 2 | `cuentas_cobrar.id_venta` (+ índice) | `0f19d8d` (cancelación de crédito) | **Crash silencioso** (stowed exception 0xc000027B) | Script manual `.ps1` |
| 3 | `promociones.id_plan` (+ índice) | `03e9a7c` (combo_membresia) | **Crash silencioso** (mismo 0xc000027B) | Script manual `.ps1` + DbInitializer resiliente |

**Desde 2026-08-26:** los casos como estos se resuelven automáticamente al arrancar — no requieren intervención manual.

### Nota de proceso (pre-auto-columnas)

Antes de commitear cualquier cambio de schema que agregue columnas a una tabla existente, correr la app localmente contra una base de datos real (no solo la suite de tests) para confirmar que el arranque no falla — la suite de tests **no detecta este tipo de error** porque usa bases de datos frescas en cada test (el `CREATE TABLE` nuevo sí incluye las columnas).

**Ahora:** el mecanismo automático detecta y agrega columnas faltantes al arrancar. La verificación manual sigue siendo recomendada pero ya no es crítica para columnas nullable/con DEFAULT.

## El script .ps1 como respaldo

`docs/migracion-dotnet/scripts/migrar-produccion-combo-membresia.ps1` se mantiene como alternativa documentada para casos donde:

- La columna es `NOT NULL` sin `DEFAULT` (el mecanismo automático la omite).
- Se necesita un backfill de datos específico.
- Se necesita verificar manualmente la integridad de datos después del ALTER.

## Referencia cruzada

Consideración análoga de rendimiento: la función SQL `sin_acentos()` (búsqueda insensible a acentos, ver commit `1cf9dd0`) aplicada dentro de un `LIKE` impide el uso de índices sobre la columna (no sargable). Aceptable para catálogos de bajo volumen; no replicarla en tablas de alto volumen (`bitacora_auditoria`, `ventas`) sin una columna normalizada persistida.

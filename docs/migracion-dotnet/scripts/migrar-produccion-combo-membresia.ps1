# =============================================================================
# Migración puntual de producción — combo_membresia (commit 03e9a7c)
# =============================================================================
# Destino: instalaciones con BD creada ANTES del commit 03e9a7c, donde la
# tabla promociones NO tiene la columna id_plan. Sin esta migración, la app
# crashea al arrancar (stowed exception 0xc000027B — ver doc 10, caso 3).
#
# Qué hace:
#   1. BACKUP automático del archivo .db (+ wal/shm) junto al original.
#   2. Verifica si falta la columna id_plan en promociones.
#      - Si falta: ALTER TABLE ADD COLUMN (nullable, sin FK para no exigir
#        integridad sobre filas históricas) + crea el índice.
#      - Si ya existe: no toca nada (idempotente).
#   3. Verificación posterior con PRAGMA table_info + conteo de filas intacto.
#
# Uso:
#   1. Cerrar la aplicación Smart Gym por completo.
#   2. Ejecutar: pwsh -NoProfile -File migrar-produccion-combo-membresia.ps1
#      (o clic derecho > Ejecutar con PowerShell).
# =============================================================================

$ErrorActionPreference = 'Stop'

$dbPath = Join-Path $env:APPDATA 'Smart-Gym-net\smart_gym.db'

if (-not (Test-Path $dbPath)) {
    Write-Error "No se encontró la base de datos en: $dbPath"
    exit 1
}

Write-Host "Base de datos: $dbPath"
Write-Host ""

# ---------------------------------------------------------------------------
# Paso 1: BACKUP automático (junto al original, con marca de tiempo)
# ---------------------------------------------------------------------------
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = "$dbPath.bak_$stamp"
Copy-Item $dbPath $backup -Force
foreach ($sufijo in @('-wal', '-shm')) {
    if (Test-Path "$dbPath$sufijo") {
        Copy-Item "$dbPath$sufijo" "$backup$sufijo" -Force
    }
}
Write-Host "Backup creado: $backup"

Add-Type -Path (Get-ChildItem "$PSScriptRoot\.." -Recurse -Filter 'Microsoft.Data.Sqlite.dll' |
    Select-Object -First 1 -ExpandProperty FullName)

$conn = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$dbPath")
$conn.Open()

function Test-Columna {
    param([string]$Tabla, [string]$Columna)
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(1) FROM pragma_table_info('$Tabla') WHERE name = '$Columna'"
    return [int]::Parse($cmd.ExecuteScalar().ToString()) -gt 0
}

function ContarFilas {
    param([string]$Tabla)
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM $Tabla"
    return [long]::Parse($cmd.ExecuteScalar().ToString())
}

try {
    $filasAntes = ContarFilas -Tabla 'promociones'
    Write-Host "Filas en promociones antes: $filasAntes"

    # -------------------------------------------------------------------------
    # Paso 2: columna id_plan (solo si falta)
    # -------------------------------------------------------------------------
    if (-not (Test-Columna -Tabla 'promociones' -Columna 'id_plan')) {
        Write-Host "Agregando columna id_plan..."
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "ALTER TABLE promociones ADD COLUMN id_plan INTEGER NULL REFERENCES planes_membresia(id_plan)"
        $cmd.ExecuteNonQuery() | Out-Null
        Write-Host "  > Columna id_plan agregada."
    }
    else {
        Write-Host "La columna id_plan ya existe — se omite el ALTER."
    }

    # -------------------------------------------------------------------------
    # Paso 3: índice (idempotente)
    # -------------------------------------------------------------------------
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_promociones_id_plan ON promociones(id_plan)"
    $cmd.ExecuteNonQuery() | Out-Null
    Write-Host "Índice idx_promociones_id_plan verificado/creado."

    # -------------------------------------------------------------------------
    # Paso 4: verificación posterior
    # -------------------------------------------------------------------------
    if (-not (Test-Columna -Tabla 'promociones' -Columna 'id_plan')) {
        throw "Fallo de verificación: la columna id_plan no quedó presente."
    }

    $filasDespues = ContarFilas -Tabla 'promociones'
    if ($filasDespues -ne $filasAntes) {
        throw "Fallo de verificación: el total de filas cambió ($filasAntes → $filasDespues)."
    }

    Write-Host ""
    Write-Host "Migración completada correctamente."
}
catch {
    Write-Error "La migración falló: $_"
    Write-Host "El backup original está en: $backup — restaura ese archivo si es necesario."
    $conn.Close()
    exit 1
}
finally {
    $conn.Close()
}

Write-Host ""
Write-Host "Listo. Puedes abrir la aplicación con normalidad."

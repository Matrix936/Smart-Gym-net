# Reset de la base de datos local (SmartGym).
# Elimina smart_gym.db del AppData de MAUI para que el próximo arranque
# reaplique el schema + seed desde cero (entorno reproducible, Fase 0/1).
$dbName = "smart_gym.db"
$roots = @(
    [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::LocalApplicationData)
)
$found = $false
foreach ($root in $roots) {
    Get-ChildItem -Path $root -Recurse -Filter $dbName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'smart[.-]?gym' } |
        ForEach-Object {
            $found = $true
            Write-Host "Eliminando: $($_.FullName)"
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
            # WAL/SHM sobrantes
            Remove-Item -LiteralPath "$($_.FullName)-wal" -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath "$($_.FullName)-shm" -Force -ErrorAction SilentlyContinue
        }
}
if (-not $found) {
    Write-Host "No se encontró ninguna BD local de Smart Gym. Nada que eliminar."
}

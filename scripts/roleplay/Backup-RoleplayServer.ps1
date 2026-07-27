param(
    [string] $InstanceRoot
)

. (Join-Path $PSScriptRoot 'RoleplayServer.Common.ps1')

$context = Get-RoleplayServerContext -InstanceRoot $InstanceRoot

if (-not (Test-Path -LiteralPath $context.ProcessFile)) {
    throw 'Start the roleplay server before creating a database backup.'
}

$settings = Read-RoleplayEnvironment -Path $context.EnvironmentFile
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = Join-Path $context.BackupDirectory "sanctuary-roleplay-$timestamp.sql"

$dump = & (Join-Path $context.MariaDbBin 'mariadb-dump.exe') `
    --host=127.0.0.1 `
    "--port=$($settings['ROLEPLAY_DB_PORT'])" `
    "--user=$($settings['ROLEPLAY_DB_USER'])" `
    "--password=$($settings['ROLEPLAY_DB_PASSWORD'])" `
    --single-transaction `
    --routines `
    --events `
    $settings['ROLEPLAY_DB_NAME']

if ($LASTEXITCODE -ne 0) {
    throw "Database backup failed with exit code $LASTEXITCODE."
}

Set-Content -LiteralPath $backupPath -Value $dump -Encoding utf8
Write-Host "Created database backup: $backupPath"

param(
    [string] $InstanceRoot
)

. (Join-Path $PSScriptRoot 'RoleplayServer.Common.ps1')

$context = Get-RoleplayServerContext -InstanceRoot $InstanceRoot

if (-not (Test-Path -LiteralPath $context.ProcessFile)) {
    Write-Host 'The roleplay server is not running.'
    return
}

$settings = Read-RoleplayEnvironment -Path $context.EnvironmentFile
$state = Get-Content -Raw -LiteralPath $context.ProcessFile | ConvertFrom-Json

foreach ($name in @('webapi', 'gateway', 'login')) {
    $entry = $state.Processes | Where-Object { $_.Name -eq $name }

    if ($null -ne $entry -and (Test-ProcessRunning -ProcessId $entry.Id)) {
        Stop-Process -Id $entry.Id
        Wait-Process -Id $entry.Id -Timeout 10 -ErrorAction SilentlyContinue
    }
}

$mariaEntry = $state.Processes | Where-Object { $_.Name -eq 'mariadb' }
if ($null -ne $mariaEntry -and (Test-ProcessRunning -ProcessId $mariaEntry.Id)) {
    Stop-MariaDb -Context $context -Settings $settings
    Wait-Process -Id $mariaEntry.Id -Timeout 15 -ErrorAction SilentlyContinue

    if (Test-ProcessRunning -ProcessId $mariaEntry.Id) {
        throw 'MariaDB did not shut down within 15 seconds.'
    }
}

Remove-Item -LiteralPath $context.ProcessFile -Force

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    & dotnet build-server shutdown | Out-Null
}

Write-Host 'Roleplay server stopped. No roleplay services remain running.'

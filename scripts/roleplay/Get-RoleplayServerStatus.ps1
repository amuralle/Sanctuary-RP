param(
    [string] $InstanceRoot,
    [switch] $Logs
)

. (Join-Path $PSScriptRoot 'RoleplayServer.Common.ps1')

$context = Get-RoleplayServerContext -InstanceRoot $InstanceRoot

if (-not (Test-Path -LiteralPath $context.ProcessFile)) {
    Write-Host 'The roleplay server is not running.'
    return
}

$state = Get-Content -Raw -LiteralPath $context.ProcessFile | ConvertFrom-Json
$status = foreach ($entry in $state.Processes) {
    [pscustomobject]@{
        Service = $entry.Name
        ProcessId = $entry.Id
        Running = Test-ProcessRunning -ProcessId $entry.Id
    }
}

$status | Format-Table -AutoSize

if ($Logs) {
    foreach ($service in @('mariadb', 'login', 'gateway', 'webapi')) {
        $consoleLogPath = Join-Path $context.InstanceRoot "logs\$service\console.err.log"

        if (Test-Path -LiteralPath $consoleLogPath) {
            Write-Host "`n[$service]"
            Get-Content -LiteralPath $consoleLogPath -Tail 30
        }

        if ($service -ne 'mariadb') {
            $applicationLog = Get-ChildItem `
                -LiteralPath (Join-Path $context.RuntimeDirectory "$service\Logs") `
                -Filter '*.log' `
                -File `
                -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1

            if ($null -ne $applicationLog) {
                Write-Host "`n[$service application]"
                Get-Content -LiteralPath $applicationLog.FullName -Tail 30
            }
        }
    }
}

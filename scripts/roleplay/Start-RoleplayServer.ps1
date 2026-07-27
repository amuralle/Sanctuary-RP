param(
    [string] $InstanceRoot,
    [switch] $RefreshServer
)

. (Join-Path $PSScriptRoot 'RoleplayServer.Common.ps1')

$context = Get-RoleplayServerContext -InstanceRoot $InstanceRoot

$requiredFiles = @(
    $context.EnvironmentFile,
    (Join-Path $context.MariaDbBin 'mariadbd.exe'),
    (Join-Path $context.RuntimeDirectory 'gateway\Sanctuary.Gateway.dll'),
    (Join-Path $context.RuntimeDirectory 'login\Sanctuary.Login.dll'),
    (Join-Path $context.RuntimeDirectory 'webapi\Sanctuary.WebAPI.dll')
)

$requiresInitialization = $RefreshServer -or
    @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -gt 0

if ($requiresInitialization) {
    & (Join-Path $PSScriptRoot 'Initialize-RoleplayServer.ps1') `
        -InstanceRoot $context.InstanceRoot `
        -RefreshServer:$RefreshServer
}

if (Test-Path -LiteralPath $context.ProcessFile) {
    $existing = Get-Content -Raw -LiteralPath $context.ProcessFile | ConvertFrom-Json
    $running = @($existing.Processes | Where-Object { Test-ProcessRunning -ProcessId $_.Id })

    if ($running.Count -gt 0) {
        throw 'The roleplay server is already running. Use Get-RoleplayServerStatus.ps1 for details.'
    }
}

$settings = Read-RoleplayEnvironment -Path $context.EnvironmentFile
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$started = @()
$environmentBackup = @{}

if (Wait-TcpPort -Port ([int]$settings['ROLEPLAY_DB_PORT']) -TimeoutSeconds 1) {
    throw "TCP port $($settings['ROLEPLAY_DB_PORT']) is already in use. Refusing to start a second MariaDB process."
}

function Set-TemporaryEnvironment {
    param(
        [string] $Name,
        [string] $Value
    )

    if (-not $environmentBackup.ContainsKey($Name)) {
        $environmentBackup[$Name] = [Environment]::GetEnvironmentVariable($Name, 'Process')
    }

    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}

function Start-RoleplayProcess {
    param(
        [string] $Name,
        [string] $WorkingDirectory,
        [string] $Dll
    )

    $logDirectory = Join-Path $context.InstanceRoot "logs\$Name"
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

    $process = Start-Process `
        -FilePath $dotnet `
        -ArgumentList $Dll `
        -WorkingDirectory $WorkingDirectory `
        -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDirectory 'console.out.log') `
        -RedirectStandardError (Join-Path $logDirectory 'console.err.log') `
        -PassThru

    $script:started += [pscustomobject]@{
        Name = $Name
        Id = $process.Id
    }

    return $process
}

try {
    Set-TemporaryEnvironment -Name 'DOTNET_ENVIRONMENT' -Value 'Production'
    Set-TemporaryEnvironment -Name 'ASPNETCORE_ENVIRONMENT' -Value 'Production'
    Set-TemporaryEnvironment -Name 'Database__Provider' -Value 'MySql'
    Set-TemporaryEnvironment -Name 'Database__VersionString' -Value $settings['ROLEPLAY_DB_VERSION']
    Set-TemporaryEnvironment -Name 'Database__ConnectionString' -Value "server=127.0.0.1;port=$($settings['ROLEPLAY_DB_PORT']);uid=$($settings['ROLEPLAY_DB_USER']);pwd=$($settings['ROLEPLAY_DB_PASSWORD']);database=$($settings['ROLEPLAY_DB_NAME'])"

    $mariaProcess = Start-Process `
        -FilePath (Join-Path $context.MariaDbBin 'mariadbd.exe') `
        -ArgumentList (Get-MariaDbArguments -Context $context) `
        -WorkingDirectory $context.MariaDbRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $context.MariaDbLogDirectory 'console.out.log') `
        -RedirectStandardError (Join-Path $context.MariaDbLogDirectory 'console.err.log') `
        -PassThru

    $started += [pscustomobject]@{
        Name = 'mariadb'
        Id = $mariaProcess.Id
    }

    if (-not (Wait-MariaDb -Context $context -Settings $settings)) {
        throw "MariaDB did not become ready. See $($context.MariaDbLogDirectory)"
    }

    $loginDirectory = Join-Path $context.RuntimeDirectory 'login'
    $login = Start-RoleplayProcess `
        -Name 'login' `
        -WorkingDirectory $loginDirectory `
        -Dll (Join-Path $loginDirectory 'Sanctuary.Login.dll')

    Start-Sleep -Seconds 2
    if ($login.HasExited) {
        throw "Sanctuary.Login exited during startup. See the roleplay login logs."
    }

    Set-TemporaryEnvironment -Name 'Server__LoginGatewayAddress' -Value '127.0.0.1:20041'
    Set-TemporaryEnvironment -Name 'Server__ServerAddress' -Value $settings['PUBLIC_GATEWAY_ADDRESS']

    $gatewayDirectory = Join-Path $context.RuntimeDirectory 'gateway'
    $gateway = Start-RoleplayProcess `
        -Name 'gateway' `
        -WorkingDirectory $gatewayDirectory `
        -Dll (Join-Path $gatewayDirectory 'Sanctuary.Gateway.dll')

    $apiBindAddress = $settings['SERVER_BIND_ADDRESS']
    Set-TemporaryEnvironment -Name 'ASPNETCORE_URLS' -Value "http://$apiBindAddress`:20040"
    Set-TemporaryEnvironment -Name 'WebAPI__LaunchArguments' -Value "AssetDelivery:IndirectServerAddress=$($settings['ASSET_SERVER_URL']) Portrait:UploadUrl=$($settings['PUBLIC_WEBAPI_URL'])/image"
    Set-TemporaryEnvironment -Name 'WebAPI__MemberByDefault' -Value $settings['MEMBER_BY_DEFAULT']

    $webApiDirectory = Join-Path $context.RuntimeDirectory 'webapi'
    $webApi = Start-RoleplayProcess `
        -Name 'webapi' `
        -WorkingDirectory $webApiDirectory `
        -Dll (Join-Path $webApiDirectory 'Sanctuary.WebAPI.dll')

    if (-not (Wait-TcpPort -Port 20040 -TimeoutSeconds 30)) {
        throw 'Sanctuary.WebAPI did not open TCP port 20040.'
    }

    foreach ($process in @($gateway, $webApi)) {
        if ($process.HasExited) {
            throw 'A Sanctuary server process exited during startup. Check the roleplay logs.'
        }
    }

    [pscustomobject]@{
        Started = [DateTimeOffset]::Now
        Processes = $started
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $context.ProcessFile -Encoding utf8
}
catch {
    if ($null -ne ($started | Where-Object { $_.Name -eq 'mariadb' }) -and
        (Wait-TcpPort -Port ([int]$settings['ROLEPLAY_DB_PORT']) -TimeoutSeconds 1)) {
        Stop-MariaDb -Context $context -Settings $settings
    }

    foreach ($entry in $started | Sort-Object { $_.Name -eq 'mariadb' }) {
        if (Test-ProcessRunning -ProcessId $entry.Id) {
            Stop-Process -Id $entry.Id -Force
        }
    }

    throw
}
finally {
    foreach ($name in $environmentBackup.Keys) {
        [Environment]::SetEnvironmentVariable($name, $environmentBackup[$name], 'Process')
    }
}

Write-Host 'Roleplay server started.'
& (Join-Path $PSScriptRoot 'Get-RoleplayServerStatus.ps1') -InstanceRoot $context.InstanceRoot

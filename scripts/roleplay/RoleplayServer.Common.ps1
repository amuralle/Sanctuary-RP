Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:MariaDbVersion = '10.5.29'
$script:MariaDbArchiveName = "mariadb-$($script:MariaDbVersion)-winx64.zip"
$script:MariaDbDownloadUrl = "https://dlm.mariadb.com/4238706/MariaDB/mariadb-$($script:MariaDbVersion)/winx64-packages/$($script:MariaDbArchiveName)"
$script:MariaDbSha256 = 'a6b11c7ed51a204d33f0a9d0344b680d1067534153ec3c9432dbcaf789b01cab'

function Get-RoleplayServerContext {
    param(
        [string] $InstanceRoot
    )

    if ([string]::IsNullOrWhiteSpace($InstanceRoot)) {
        $documents = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'Documents'
        $InstanceRoot = Join-Path $documents 'FreeRealms-Instances\roleplay'
    }

    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $resolvedInstanceRoot = [IO.Path]::GetFullPath($InstanceRoot)
    $mariaDbRoot = Join-Path $resolvedInstanceRoot "tools\mariadb-$($script:MariaDbVersion)-winx64"
    $mariaDbData = Join-Path $resolvedInstanceRoot 'mariadb\data'

    [pscustomobject]@{
        RepositoryRoot = $repositoryRoot
        InstanceRoot = $resolvedInstanceRoot
        EnvironmentFile = Join-Path $resolvedInstanceRoot 'roleplay.env'
        BackupDirectory = Join-Path $resolvedInstanceRoot 'backups'
        PackageDirectory = Join-Path $resolvedInstanceRoot 'packages'
        RuntimeDirectory = Join-Path $resolvedInstanceRoot 'server'
        ProcessFile = Join-Path $resolvedInstanceRoot 'server-processes.json'
        MariaDbRoot = $mariaDbRoot
        MariaDbBin = Join-Path $mariaDbRoot 'bin'
        MariaDbData = $mariaDbData
        MariaDbConfig = Join-Path $mariaDbData 'my.ini'
        MariaDbLogDirectory = Join-Path $resolvedInstanceRoot 'logs\mariadb'
    }
}

function Read-RoleplayEnvironment {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $values = @{}

    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
            continue
        }

        $parts = $trimmed.Split('=', 2)
        if ($parts.Count -eq 2) {
            $values[$parts[0]] = $parts[1]
        }
    }

    return $values
}

function Assert-Executable {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found at $Path"
    }
}

function Test-ProcessRunning {
    param(
        [int] $ProcessId
    )

    if ($ProcessId -le 0) {
        return $false
    }

    return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Wait-TcpPort {
    param(
        [string] $HostName = '127.0.0.1',
        [Parameter(Mandatory)]
        [int] $Port,
        [int] $TimeoutSeconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadline) {
        $client = [Net.Sockets.TcpClient]::new()

        try {
            $task = $client.ConnectAsync($HostName, $Port)
            if ($task.Wait(500) -and $client.Connected) {
                return $true
            }
        }
        catch {
        }
        finally {
            $client.Dispose()
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Get-MariaDbArguments {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Context
    )

    return @(
        "--defaults-file=$($Context.MariaDbConfig)",
        '--bind-address=127.0.0.1',
        '--character-set-server=utf8mb4',
        '--collation-server=utf8mb4_unicode_ci',
        '--console'
    )
}

function Wait-MariaDb {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Context,

        [Parameter(Mandatory)]
        [hashtable] $Settings,

        [int] $TimeoutSeconds = 30
    )

    $admin = Join-Path $Context.MariaDbBin 'mariadb-admin.exe'
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadline) {
        & $admin `
            --host=127.0.0.1 `
            "--port=$($Settings['ROLEPLAY_DB_PORT'])" `
            --user=root `
            "--password=$($Settings['ROLEPLAY_DB_ROOT_PASSWORD'])" `
            ping 2>$null | Out-Null

        if ($LASTEXITCODE -eq 0) {
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Stop-MariaDb {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Context,

        [Parameter(Mandatory)]
        [hashtable] $Settings
    )

    $admin = Join-Path $Context.MariaDbBin 'mariadb-admin.exe'

    & $admin `
        --host=127.0.0.1 `
        "--port=$($Settings['ROLEPLAY_DB_PORT'])" `
        --user=root `
        "--password=$($Settings['ROLEPLAY_DB_ROOT_PASSWORD'])" `
        shutdown 2>$null
}

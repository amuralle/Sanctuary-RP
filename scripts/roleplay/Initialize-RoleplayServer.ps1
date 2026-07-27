param(
    [string] $InstanceRoot,
    [switch] $RefreshServer
)

. (Join-Path $PSScriptRoot 'RoleplayServer.Common.ps1')

$context = Get-RoleplayServerContext -InstanceRoot $InstanceRoot

$directories = @(
    $context.InstanceRoot,
    $context.BackupDirectory,
    $context.PackageDirectory,
    $context.RuntimeDirectory,
    $context.MariaDbLogDirectory,
    (Join-Path $context.InstanceRoot 'images'),
    (Join-Path $context.InstanceRoot 'logs\gateway'),
    (Join-Path $context.InstanceRoot 'logs\login'),
    (Join-Path $context.InstanceRoot 'logs\webapi')
)

foreach ($directory in $directories) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

function New-HexSecret {
    $bytes = [byte[]]::new(32)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return [BitConverter]::ToString($bytes).Replace('-', '')
}

if (-not (Test-Path -LiteralPath $context.EnvironmentFile)) {
    $environment = @(
        '# Generated roleplay-server settings. Keep this file private.'
        'ROLEPLAY_DB_NAME=sanctuary_roleplay'
        'ROLEPLAY_DB_USER=sanctuary'
        "ROLEPLAY_DB_PASSWORD=$(New-HexSecret)"
        "ROLEPLAY_DB_ROOT_PASSWORD=$(New-HexSecret)"
        'ROLEPLAY_DB_PORT=3307'
        "ROLEPLAY_DB_VERSION=$($script:MariaDbVersion)-mariadb"
        'SERVER_BIND_ADDRESS=127.0.0.1'
        'PUBLIC_GATEWAY_ADDRESS=127.0.0.1:20260'
        'PUBLIC_WEBAPI_URL=http://127.0.0.1:20040'
        'ASSET_SERVER_URL=http://osfr.editz.dev/assets'
        'MEMBER_BY_DEFAULT=true'
    )

    Set-Content -LiteralPath $context.EnvironmentFile -Value $environment -Encoding ascii
}

$settings = Read-RoleplayEnvironment -Path $context.EnvironmentFile

if (-not $settings.ContainsKey('ROLEPLAY_DB_PORT')) {
    Add-Content -LiteralPath $context.EnvironmentFile -Value 'ROLEPLAY_DB_PORT=3307' -Encoding ascii
    $settings['ROLEPLAY_DB_PORT'] = '3307'
}

if (-not $settings.ContainsKey('ROLEPLAY_DB_VERSION')) {
    Add-Content -LiteralPath $context.EnvironmentFile -Value "ROLEPLAY_DB_VERSION=$($script:MariaDbVersion)-mariadb" -Encoding ascii
    $settings['ROLEPLAY_DB_VERSION'] = "$($script:MariaDbVersion)-mariadb"
}

$archivePath = Join-Path $context.PackageDirectory $script:MariaDbArchiveName

if (-not (Test-Path -LiteralPath $archivePath)) {
    Write-Host "Downloading portable MariaDB $($script:MariaDbVersion)..."
    Invoke-WebRequest -UseBasicParsing -Uri $script:MariaDbDownloadUrl -OutFile $archivePath
}

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($archiveHash -ne $script:MariaDbSha256) {
    throw "MariaDB archive checksum mismatch: $archivePath"
}

if (-not (Test-Path -LiteralPath $context.MariaDbRoot)) {
    Write-Host 'Extracting portable MariaDB...'
    Expand-Archive -LiteralPath $archivePath -DestinationPath (Split-Path $context.MariaDbRoot -Parent)
}

$installDatabase = Join-Path $context.MariaDbBin 'mariadb-install-db.exe'
$serverExecutable = Join-Path $context.MariaDbBin 'mariadbd.exe'
Assert-Executable -Path $installDatabase -Description 'MariaDB initializer'
Assert-Executable -Path $serverExecutable -Description 'MariaDB server'

if (-not (Test-Path -LiteralPath (Join-Path $context.MariaDbData 'mysql'))) {
    New-Item -ItemType Directory -Path $context.MariaDbData -Force | Out-Null
    Push-Location $context.MariaDbRoot

    try {
        & $installDatabase `
            "--datadir=$($context.MariaDbData)" `
            "--password=$($settings['ROLEPLAY_DB_ROOT_PASSWORD'])" `
            "--port=$($settings['ROLEPLAY_DB_PORT'])"
    }
    finally {
        Pop-Location
    }

    if ($LASTEXITCODE -ne 0) {
        throw "MariaDB initialization failed with exit code $LASTEXITCODE."
    }
}

$gatewayDll = Join-Path $context.RuntimeDirectory 'gateway\Sanctuary.Gateway.dll'
$loginDll = Join-Path $context.RuntimeDirectory 'login\Sanctuary.Login.dll'
$webApiDll = Join-Path $context.RuntimeDirectory 'webapi\Sanctuary.WebAPI.dll'

if ($RefreshServer -or
    -not (Test-Path -LiteralPath $gatewayDll) -or
    -not (Test-Path -LiteralPath $loginDll) -or
    -not (Test-Path -LiteralPath $webApiDll)) {
    Assert-Executable -Path (Get-Command dotnet -ErrorAction Stop).Source -Description '.NET SDK'

    $projects = @(
        @{ Name = 'gateway'; Project = 'src\Sanctuary.Gateway\Sanctuary.Gateway.csproj' },
        @{ Name = 'login'; Project = 'src\Sanctuary.Login\Sanctuary.Login.csproj' },
        @{ Name = 'webapi'; Project = 'src\Sanctuary.WebAPI\Sanctuary.WebAPI.csproj' }
    )

    foreach ($project in $projects) {
        $outputDirectory = Join-Path $context.RuntimeDirectory $project.Name
        Write-Host "Publishing $($project.Name)..."

        & dotnet publish `
            (Join-Path $context.RepositoryRoot $project.Project) `
            --configuration Release `
            --output $outputDirectory

        if ($LASTEXITCODE -ne 0) {
            throw "Publishing $($project.Name) failed with exit code $LASTEXITCODE."
        }
    }

    foreach ($target in @('gateway', 'login')) {
        $resourceTarget = Join-Path $context.RuntimeDirectory "$target\Resources"
        New-Item -ItemType Directory -Path $resourceTarget -Force | Out-Null
        Copy-Item `
            -Path (Join-Path $context.RepositoryRoot 'src\Resources\*') `
            -Destination $resourceTarget `
            -Recurse `
            -Force
    }
}

$mariaLogOut = Join-Path $context.MariaDbLogDirectory 'initialize.out.log'
$mariaLogError = Join-Path $context.MariaDbLogDirectory 'initialize.err.log'
$mariaProcess = Start-Process `
    -FilePath $serverExecutable `
    -ArgumentList (Get-MariaDbArguments -Context $context) `
    -WorkingDirectory $context.MariaDbRoot `
    -WindowStyle Hidden `
    -RedirectStandardOutput $mariaLogOut `
    -RedirectStandardError $mariaLogError `
    -PassThru

try {
    if (-not (Wait-MariaDb -Context $context -Settings $settings)) {
        throw "MariaDB did not become ready. See $mariaLogError"
    }

    $database = $settings['ROLEPLAY_DB_NAME']
    $username = $settings['ROLEPLAY_DB_USER']
    $password = $settings['ROLEPLAY_DB_PASSWORD']
    $sql = @"
CREATE DATABASE IF NOT EXISTS ``$database`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '$username'@'127.0.0.1' IDENTIFIED BY '$password';
ALTER USER '$username'@'127.0.0.1' IDENTIFIED BY '$password';
GRANT ALL PRIVILEGES ON ``$database``.* TO '$username'@'127.0.0.1';
FLUSH PRIVILEGES;
"@

    & (Join-Path $context.MariaDbBin 'mariadb.exe') `
        --host=127.0.0.1 `
        "--port=$($settings['ROLEPLAY_DB_PORT'])" `
        --user=root `
        "--password=$($settings['ROLEPLAY_DB_ROOT_PASSWORD'])" `
        "--execute=$sql"

    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to provision the roleplay database.'
    }
}
finally {
    Stop-MariaDb -Context $context -Settings $settings
    $mariaProcess.WaitForExit(10000) | Out-Null
}

Write-Host "Roleplay server initialized at $($context.InstanceRoot)"

param(
    [string] $SshHost = 'osfr-rp',
    [string] $Domain = 'play.osfr-rp.com',
    [string] $WebsiteDomain = 'www.osfr-rp.com',
    [string] $ClientRoot = "$env:LOCALAPPDATA\OSFRLauncher\Servers\OSFR Public Server\Client",
    [switch] $SkipClientUpload,
    [switch] $RefreshClient
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Domain -notmatch '^[a-zA-Z0-9.-]+$') {
    throw "Invalid deployment domain: $Domain"
}

if ($WebsiteDomain -notmatch '^[a-zA-Z0-9.-]+$') {
    throw "Invalid website domain: $WebsiteDomain"
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$revision = (& git -C $repositoryRoot rev-parse --short HEAD).Trim()

if (& git -C $repositoryRoot status --porcelain) {
    $revision = "$revision-dirty"
}

$releaseId = "{0}-{1}" -f (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'), $revision
$stagingRoot = Join-Path $env:TEMP "sanctuary-vps-$releaseId"
$resolvedTempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
$resolvedStagingRoot = [IO.Path]::GetFullPath($stagingRoot)

if (-not $resolvedStagingRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use staging path outside the temporary directory: $resolvedStagingRoot"
}

$releaseRoot = Join-Path $stagingRoot 'release'
$releaseArchive = Join-Path $stagingRoot "$releaseId.tar"
$clientArchive = Join-Path $stagingRoot 'client.tar'
$clientManifest = Join-Path $stagingRoot 'clientmanifest.xml'

try {
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

    $projects = @(
        @{ Name = 'gateway'; Project = 'src\Sanctuary.Gateway\Sanctuary.Gateway.csproj' },
        @{ Name = 'login'; Project = 'src\Sanctuary.Login\Sanctuary.Login.csproj' },
        @{ Name = 'webapi'; Project = 'src\Sanctuary.WebAPI\Sanctuary.WebAPI.csproj' }
    )

    foreach ($project in $projects) {
        $outputPath = Join-Path $releaseRoot $project.Name
        Write-Host "Publishing $($project.Name) for linux-x64..."

        & dotnet publish `
            (Join-Path $repositoryRoot $project.Project) `
            --configuration Release `
            --runtime linux-x64 `
            --self-contained true `
            --output $outputPath

        if ($LASTEXITCODE -ne 0) {
            throw "Publishing $($project.Name) failed with exit code $LASTEXITCODE."
        }
    }

    foreach ($service in @('gateway', 'login')) {
        Copy-Item `
            -Path (Join-Path $repositoryRoot 'src\Resources') `
            -Destination (Join-Path $releaseRoot $service) `
            -Recurse `
            -Force
    }

    Copy-Item `
        -Path (Join-Path $repositoryRoot 'src\Scripts') `
        -Destination (Join-Path $releaseRoot 'gateway') `
        -Recurse `
        -Force

    Copy-Item `
        -Path (Join-Path $repositoryRoot 'site') `
        -Destination (Join-Path $releaseRoot 'site') `
        -Recurse `
        -Force

    $downloadPath = Join-Path $releaseRoot 'site\downloads'
    New-Item -ItemType Directory -Path $downloadPath -Force | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'output\pdf\OSFR_Roleplay_Model_Field_Guide.pdf') `
        -Destination $downloadPath `
        -Force
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'output\model-catalog\OSFR_Roleplay_Model_Catalog.csv') `
        -Destination $downloadPath `
        -Force

    & tar -cf $releaseArchive -C $releaseRoot .
    if ($LASTEXITCODE -ne 0) {
        throw 'Creating the release archive failed.'
    }

    $remoteArchive = "/tmp/$releaseId.tar"
    $remoteInstaller = "/tmp/Install-RoleplayRelease.sh"

    & scp $releaseArchive "${SshHost}:$remoteArchive"
    if ($LASTEXITCODE -ne 0) {
        throw 'Uploading the release archive failed.'
    }

    & scp (Join-Path $PSScriptRoot 'Install-RoleplayRelease.sh') "${SshHost}:$remoteInstaller"
    if ($LASTEXITCODE -ne 0) {
        throw 'Uploading the release installer failed.'
    }

    & ssh $SshHost "chmod 700 '$remoteInstaller' && sudo '$remoteInstaller' '$remoteArchive' '$releaseId' '$Domain' '$WebsiteDomain'"
    if ($LASTEXITCODE -ne 0) {
        throw 'Installing the release failed.'
    }

    if (-not $SkipClientUpload) {
        if (-not (Test-Path -LiteralPath (Join-Path $ClientRoot 'FreeRealms.exe') -PathType Leaf)) {
            throw "A complete client was not found at $ClientRoot"
        }

        $remoteHasClient = (& ssh $SshHost "test -f /srv/osfr-rp/www/client/FreeRealms.exe && echo yes || echo no").Trim()

        if ($RefreshClient -or $remoteHasClient -ne 'yes') {
            Write-Host 'Packaging the launcher-verified public client for its initial upload...'
            & tar -cf $clientArchive -C $ClientRoot .
            if ($LASTEXITCODE -ne 0) {
                throw 'Creating the client archive failed.'
            }

            & scp $clientArchive "${SshHost}:/tmp/osfr-client.tar"
            if ($LASTEXITCODE -ne 0) {
                throw 'Uploading the client archive failed.'
            }

            & ssh $SshHost "sudo install -d -m 0755 -o caddy -g caddy /srv/osfr-rp/www/client && sudo tar -xf /tmp/osfr-client.tar -C /srv/osfr-rp/www/client && sudo chown -R caddy:caddy /srv/osfr-rp/www/client && rm -f /tmp/osfr-client.tar"
            if ($LASTEXITCODE -ne 0) {
                throw 'Installing the client files failed.'
            }
        }

        Invoke-WebRequest `
            -UseBasicParsing `
            -Uri 'https://opensourcefreerealms.com/clientmanifest.xml' `
            -OutFile $clientManifest

        & scp $clientManifest "${SshHost}:/tmp/clientmanifest-template.xml"
        if ($LASTEXITCODE -ne 0) {
            throw 'Uploading the client manifest template failed.'
        }

        & scp (Join-Path $PSScriptRoot 'Build-RoleplayClientManifest.py') "${SshHost}:/tmp/Build-RoleplayClientManifest.py"
        if ($LASTEXITCODE -ne 0) {
            throw 'Uploading the client manifest builder failed.'
        }

        & ssh $SshHost "sudo python3 /tmp/Build-RoleplayClientManifest.py --root /srv/osfr-rp/www/client --template /tmp/clientmanifest-template.xml --output /tmp/clientmanifest.xml && sudo install -m 0644 -o caddy -g caddy /tmp/clientmanifest.xml /srv/osfr-rp/www/clientmanifest.xml && sudo rm -f /tmp/clientmanifest-template.xml /tmp/clientmanifest.xml /tmp/Build-RoleplayClientManifest.py"
        if ($LASTEXITCODE -ne 0) {
            throw 'Generating or installing the client manifest failed.'
        }
    }

    Write-Host "Release $releaseId is installed on $SshHost."
}
finally {
    Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force -ErrorAction SilentlyContinue
}

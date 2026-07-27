param(
    [string] $SettingsPath = (Join-Path $env:LOCALAPPDATA 'OSFRLauncher\Launcher.xml'),
    [string] $ClientSourceName = 'Local Sanctuary',
    [string] $RoleplayServerName = 'Athropex Roleplay Local'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (Get-Process -Name 'Launcher' -ErrorAction SilentlyContinue) {
    throw 'Close the OSFR Launcher before changing its server list.'
}

if (-not (Test-Path -LiteralPath $SettingsPath -PathType Leaf)) {
    throw "Launcher settings were not found at $SettingsPath"
}

[xml] $settings = Get-Content -Raw -LiteralPath $SettingsPath
$serverList = $settings.Settings.ServerInfoList

if ($null -eq $serverList) {
    throw 'The launcher settings do not contain a ServerInfoList.'
}

$clientSource = @($serverList.ServerInfo | Where-Object { $_.Name -eq $ClientSourceName }) | Select-Object -First 1
if ($null -eq $clientSource -or [string]::IsNullOrWhiteSpace($clientSource.SavePath)) {
    throw "Launcher server '$ClientSourceName' does not provide a reusable client path."
}

$clientExecutable = Join-Path ([string] $clientSource.SavePath) 'Client\FreeRealms.exe'
if (-not (Test-Path -LiteralPath $clientExecutable -PathType Leaf)) {
    throw "The reusable Free Realms client was not found at $clientExecutable"
}

$roleplayServer = @($serverList.ServerInfo | Where-Object { $_.Name -eq $RoleplayServerName }) | Select-Object -First 1

if ($null -eq $roleplayServer) {
    $roleplayServer = $settings.CreateElement('ServerInfo')
    $serverList.AppendChild($roleplayServer) | Out-Null
}

function Set-ElementValue {
    param(
        [Parameter(Mandatory)]
        [Xml.XmlElement] $Parent,

        [Parameter(Mandatory)]
        [string] $Name,

        [AllowEmptyString()]
        [string] $Value
    )

    $element = $Parent.SelectSingleNode($Name)
    if ($null -eq $element) {
        $element = $settings.CreateElement($Name)
        $Parent.AppendChild($element) | Out-Null
    }

    $element.InnerText = $Value
}

Set-ElementValue -Parent $roleplayServer -Name 'Url' -Value ''
Set-ElementValue -Parent $roleplayServer -Name 'Name' -Value $RoleplayServerName
Set-ElementValue -Parent $roleplayServer -Name 'Description' -Value 'Private local roleplay and virtual tabletop server.'
Set-ElementValue -Parent $roleplayServer -Name 'WebApiUrl' -Value 'http://127.0.0.1:20040'
Set-ElementValue -Parent $roleplayServer -Name 'LoginServer' -Value '127.0.0.1:20042'
Set-ElementValue -Parent $roleplayServer -Name 'SavePath' -Value ([string] $clientSource.SavePath)
Set-ElementValue -Parent $roleplayServer -Name 'Username' -Value ''
Set-ElementValue -Parent $roleplayServer -Name 'RememberUsername' -Value 'false'
Set-ElementValue -Parent $roleplayServer -Name 'Password' -Value ''
Set-ElementValue -Parent $roleplayServer -Name 'RememberPassword' -Value 'false'

$backupPath = "$SettingsPath.roleplay-backup"
Copy-Item -LiteralPath $SettingsPath -Destination $backupPath -Force

$writerSettings = [Xml.XmlWriterSettings]::new()
$writerSettings.Indent = $true
$writerSettings.Encoding = [Text.UTF8Encoding]::new($false)

$writer = [Xml.XmlWriter]::Create($SettingsPath, $writerSettings)

try {
    $settings.Save($writer)
}
finally {
    $writer.Dispose()
}

Write-Host "Configured launcher entry '$RoleplayServerName'."
Write-Host "Reusing client: $($clientSource.SavePath)"

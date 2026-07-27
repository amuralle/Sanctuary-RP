# Roleplay Server

This branch keeps the roleplay server independent from archaeology development. It
runs natively on Windows without Docker, WSL, or a persistent Windows service.

MariaDB data, credentials, published server binaries, logs, portraits, and backups
live outside the repository under
`C:\Users\<user>\Documents\FreeRealms-Instances\roleplay`. Keep that directory
outside OneDrive or another live file-sync client.

## Commands

Run these from PowerShell:

```powershell
.\scripts\roleplay\Initialize-RoleplayServer.ps1
.\scripts\roleplay\Initialize-RoleplayServer.ps1 -RefreshServer
.\scripts\roleplay\Configure-RoleplayLauncher.ps1
.\scripts\roleplay\Start-RoleplayServer.ps1
.\scripts\roleplay\Get-RoleplayServerStatus.ps1
.\scripts\roleplay\Get-RoleplayServerStatus.ps1 -Logs
.\scripts\roleplay\Backup-RoleplayServer.ps1
.\scripts\roleplay\Stop-RoleplayServer.ps1
```

Initialization downloads the official MariaDB 10.5.29 Windows ZIP archive and
verifies its SHA-256 checksum before extraction. MariaDB binds only to
`127.0.0.1:3307` and runs only while the roleplay server is active.

The generated `roleplay.env` defaults all game services to localhost:

- Sanctuary WebAPI: TCP 20040
- Sanctuary Login: UDP 20042
- Sanctuary Gateway: UDP 20260
- MariaDB: TCP 3307, loopback only

Use `-RefreshServer` after changing roleplay source or resources. Internet hosting
should use a separate public configuration after router, firewall, DNS, account
security, and restore testing are complete.

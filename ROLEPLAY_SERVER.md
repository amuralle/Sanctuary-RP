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

## Linux VPS

The VPS deployment remains separate from the local Windows runtime. It publishes
self-contained `linux-x64` services, installs them as systemd units, keeps MariaDB
and WebAPI on loopback, and exposes the web edge plus the two public UDP game ports.

From PowerShell:

```powershell
.\scripts\roleplay-vps\Deploy-RoleplayVps.ps1
```

The first deployment uploads the launcher-verified public client. It uses the
official manifest as a file allowlist, then regenerates sizes and hashes from the
hosted copy so launcher verification remains self-consistent. Later deployments
reuse those files unless `-RefreshClient` is supplied. The remote installer also
configures Caddy, authentication rate limits, a daily database and resource backup,
and a 14-day local backup retention window.

Public DNS must point the deployment hostname at the VPS with Cloudflare proxying
disabled because the game login and gateway use UDP. The default hostname is
`play.osfr-rp.com`.

## Tabletop miniatures

Every player can place session-only NPC miniatures for roleplay scenes. Miniatures
inherit the player's position and facing, use short per-zone IDs, and can be
organized into optional groups:

```text
!mini spawn 724 @enemies Skeletal Guard
!mini models fossil
!mini models * 2
!mini list @enemies
!mini info nearest
!mini groups
!mini remove #1
!mini remove nearest 20
!mini clear mine
!mini clear @enemies
!mini clear nearby 30
!mini clear all
```

Clicking a miniature reports its ID, model, group, and original owner. Any player
can remove any roleplay miniature, but these commands cannot remove built-in world
NPCs, mounts, or collection nodes. A zone restart clears all miniatures.

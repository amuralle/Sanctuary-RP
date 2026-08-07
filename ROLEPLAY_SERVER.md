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
configures Caddy, authentication rate limits, a daily database, resource, and model
image backup, and a 14-day local backup retention window.

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
!mini move #1 here
!mini move #1 120.5 32.0 -84.25 path
!mini stop #1
!mini emote #1 wave
!mini emotes
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

Admins also have session-only DM presentation controls. Model IDs use the same
catalog as miniature spawning, and invisible mode hides the DM from other players
without removing the DM's own view of the scene:

```text
!dm model 176
!dm model reset
!dm invisible on
!dm invisible off
!dm status
```

The searchable model guide and complete CSV catalog can be regenerated from the
current client resources:

```powershell
python scripts/roleplay-model-guide/Generate-RoleplayModelGuide.py `
    --models src/Resources/Models.txt `
    --npcs src/Resources/Npcs.json `
    --pdf output/pdf/OSFR_Roleplay_Model_Field_Guide.pdf `
    --csv output/model-catalog/OSFR_Roleplay_Model_Catalog.csv
```

## Model images

Players can sign in to the website with their game account and contribute one
preview image per catalog model. Website authentication uses its own encrypted
cookie and does not create or consume the short-lived launcher session stored in
the user table.

Uploads accept JPEG, PNG, or WebP files up to 4 MB and reject unknown model IDs.
The WebAPI removes image metadata, converts the file to WebP, limits it to
1280x960, and progressively optimizes it to 512 KB or less. Uploading another
image for the same model atomically replaces the existing file and updates the
displayed `Last updated by` account credit.

On the VPS, persistent files and attribution metadata live under
`/var/lib/sanctuary/model-catalog`. Data protection keys live separately under
`/var/lib/sanctuary/data-protection`; losing those keys signs website users out
but does not affect game accounts.

## Collection volunteer work

The roleplay server keeps community hardpoint surveying separate from the live
collection resources. Every player can submit their current position and facing
to a persistent review queue:

```text
!volunteer pools
!volunteer place mushrooms beside the blue tree
!volunteer mine
!volunteer undo last
```

The website's Collection Map view can filter submissions by zone, pool, and
review status and plots X/Z coordinates for one zone at a time. Contributors can
edit or remove their own pending submissions after signing in with their game
account. Administrators can correct coordinates and mark submissions accepted or
rejected. Review status is organizational only: it never mutates active collection
nodes or the upstream collection resource files automatically.

Gateway and WebAPI share the persistent JSON file at
`/var/lib/sanctuary/collection-volunteers/placements.json`. The file is protected
by a cross-process lock, written atomically, and included in the daily VPS backup.

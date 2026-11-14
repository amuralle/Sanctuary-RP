# OSFR-RP / Sanctuary-RP

**A roleplay and virtual tabletop server for Free Realms, built on Sanctuary.**

[Website and toolkit](https://play.osfr-rp.com/guide/) | [Join our Discord](https://discord.gg/hrA85MU9Px) | [Setup and commands](ROLEPLAY_SERVER.md) | [Updates](CHANGELOG.md) | [Contribute](CONTRIBUTING.md)

[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue)](LICENSE)

## What this project is

OSFR-RP uses the Free Realms world as a shared space for roleplay, player-created
scenes, and DM-led sessions. Sanctuary-RP contains the C# server, RP commands,
companion web toolkit, and scripts for running your own instance.

It is an independently maintained derivative of
[Open Source Free Realms' Sanctuary](https://github.com/Open-Source-Free-Realms/Sanctuary).
The upstream project develops the general Free Realms server emulator; this
repository develops the RP experience and integrates upstream changes separately.
RP features and hosting decisions here are maintained separately from the main
OSFR server. Shared fixes can be proposed upstream without making RP-specific
behavior part of the main project.

## Start here

| You want to... | Go to... |
| --- | --- |
| Join the community, ask about joining a session, or discuss RP ideas | [Free Realms Roleplay Community Hub on Discord](https://discord.gg/hrA85MU9Px) |
| Browse models, prepare scene commands, or use the Collection Map | [The live roleplay toolkit](https://play.osfr-rp.com/guide/) |
| Learn miniature and DM commands, permissions, or persistence | [RP server reference](ROLEPLAY_SERVER.md#tabletop-miniatures) |
| Run an instance you control | [Build instructions below](#build-from-source) and [instance setup](ROLEPLAY_SERVER.md) |
| Work on the website or API | [Website source guide](site/README.md) and [contribution guide](CONTRIBUTING.md) |
| Follow published changes or prepare an update | [Change history](CHANGELOG.md) and [publishing guide](docs/RELEASING.md) |
| Report a security concern privately | [Security policy](SECURITY.md) |

The Discord invite is for the community; joining it does not create a game
account. The toolkit uses an existing game account for contributions. Ask in the
community about current sessions and connection instructions.

## What makes it different

- **Shared scene miniatures:** spawn NPC models, name and group them, move them,
  play emotes, inspect their IDs, and clear a scene with `!mini` commands.
- **DM presentation controls:** administrators can temporarily change their model
  or become invisible to other players with `!dm` commands.
- **A companion roleplay toolkit:** browse and search client models, prepare spawn
  commands, consult the command reference, and contribute model preview images
  using a game account. The website source is in [`site/`](site/).
- **Community collection surveying:** players can submit positions with
  `!volunteer`, then review them on the toolkit's Collection Map. Accepting a
  submission records review status; it does not automatically change live nodes
  or upstream resources.
- **Separate instance tooling:** native Windows scripts and a Linux VPS deployment
  path keep server data, credentials, and backups outside the source checkout.

Miniatures and DM presentation changes are session-only. Miniatures disappear
when their zone restarts; the scene tools are not a persistent campaign save
system. See [the RP reference](ROLEPLAY_SERVER.md) for commands and permissions.

## Project status and branches

This is an actively developed server emulator and RP toolkit. Features can be
incomplete, and source availability does not imply that every Free Realms system
is implemented or that the hosted server is currently online.

| Branch | Purpose |
| --- | --- |
| [`main`](https://github.com/amuralle/Sanctuary-RP/tree/main) | Default public RP source, including upstream integrations. Start here. |
| [`roleplay/tabletop-controls`](https://github.com/amuralle/Sanctuary-RP/tree/roleplay/tabletop-controls) | Separate RP/tabletop development line; its upstream base and feature set can differ from `main`. |

Both branches are public. Local experiments and uncommitted work are not included
until explicitly committed and pushed. The hosted instance may run a separately
published build; this README does not assert which commit is deployed. There are
currently no packaged GitHub releases; build from source using the instructions
below.

## Playing and running a server

For the hosted RP project, start at [the toolkit](https://play.osfr-rp.com/guide/)
and [our Discord community](https://discord.gg/hrA85MU9Px).
This repository provides server software and the web toolkit. A compatible
Free Realms client and launcher are also needed to play; see the
[OSFR Launcher project](https://github.com/Open-Source-Free-Realms/Launcher).

### Build from source

Install Git and the .NET SDK requested by [`src/global.json`](src/global.json)
on your chosen branch. SDK requirements can differ between branches; use an
editor or IDE that supports that SDK.

```powershell
git clone https://github.com/amuralle/Sanctuary-RP.git
cd Sanctuary-RP
cd src
dotnet restore
dotnet build --configuration Release
```

Building the solution does not configure or start an instance. The server uses
separate Login, Gateway, and WebAPI services, plus a database. The RP Windows
scripts provision MariaDB; the underlying emulator also supports SQLite. See
[database configuration](src/Sanctuary.Database/README.md) for provider details.

### Local Windows RP instance

From the repository root, use the native PowerShell scripts:

```powershell
.\scripts\roleplay\Initialize-RoleplayServer.ps1
.\scripts\roleplay\Start-RoleplayServer.ps1
.\scripts\roleplay\Get-RoleplayServerStatus.ps1
```

Initialization downloads and verifies portable MariaDB, builds the server, and
creates a separate instance under `Documents/FreeRealms-Instances/roleplay` by
default. Local services bind to localhost. To connect a client, configure the
launcher as described in [the setup guide](ROLEPLAY_SERVER.md#local-windows-instance).
That guide also covers refreshing builds, stopping the instance, and backups.

For Linux hosting, read [the VPS deployment notes](ROLEPLAY_SERVER.md#linux-vps)
and review the deployment scripts and host settings before running them.

## Website and source code

The website is part of this repository. Its public frontend and the API that
supports it can be developed together; a separate website repository is not
required.

| Area | Source |
| --- | --- |
| Website layout, behavior, styling, and public catalog data | [`site/`](site/) |
| Website development and preview instructions | [`site/README.md`](site/README.md) |
| Login, image contributions, collection review, and public statistics API | [`src/Sanctuary.WebAPI/`](src/Sanctuary.WebAPI/) |
| Shared collection survey storage and review rules | [`src/Sanctuary.Core/CollectionVolunteers/`](src/Sanctuary.Core/CollectionVolunteers/) |
| In-game scene, DM, and survey commands | [`src/Sanctuary.Game/ChatCommands/`](src/Sanctuary.Game/ChatCommands/) |
| Offline model catalog and field-guide generation | [`scripts/roleplay-model-guide/`](scripts/roleplay-model-guide/) |
| Windows instance and Linux deployment scripts | [`scripts/roleplay/`](scripts/roleplay/) and [`scripts/roleplay-vps/`](scripts/roleplay-vps/) |

The repository contains reusable source and public catalog assets. Production
credentials, SSH keys, player databases, website sessions, upload attribution
stores, and backups belong outside Git. Publishing source does not grant access
to the running host or its data. See the [website guide](site/README.md) for the
boundary between static files and server-backed features.

## Community, updates, and support

Join [Free Realms Roleplay Community Hub](https://discord.gg/hrA85MU9Px) for
community discussion, session coordination, and questions. Use
[GitHub issues](https://github.com/amuralle/Sanctuary-RP/issues) for reproducible
bugs and scoped feature proposals so the work can be tracked.

Published documentation changes are recorded in [CHANGELOG.md](CHANGELOG.md).
The [commit history](https://github.com/amuralle/Sanctuary-RP/commits/main/) shows
source updates; it is not a record of what is currently deployed. Version tags
and release notes should identify their exact commit and any migration or
configuration requirements; see [the publishing guide](docs/RELEASING.md).

## Contributing and reporting problems

Open RP bugs and feature requests in
[Sanctuary-RP issues](https://github.com/amuralle/Sanctuary-RP/issues), and target
pull requests at this repository. Include the branch, commit, reproduction steps,
and relevant commands. See [CONTRIBUTING.md](CONTRIBUTING.md) for branch selection
and validation guidance. Upstream's wiki can provide emulator background, but the
RP setup and command reference are maintained here.

## License and credits

See [LICENSE](LICENSE) for the GNU Affero General Public License, version 3.
Sanctuary-RP builds on the work of the
[Sanctuary contributors](https://github.com/Open-Source-Free-Realms/Sanctuary/graphs/contributors)
and the broader Open Source Free Realms community. Upstream history and license
are retained. Free Realms is the original game on which this emulator is based;
this repository is a community project.

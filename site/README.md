# OSFR-RP website and toolkit source

[Live toolkit](https://play.osfr-rp.com/guide/) | [Discord community](https://discord.gg/hrA85MU9Px) | [Project overview](../README.md)

The toolkit is the web companion to Sanctuary-RP. It helps players find client
models, copy scene commands, contribute preview images, and review community
collection surveys. This directory is the frontend source, maintained alongside
the game server and its WebAPI in this repository.

## Source map

| Path | Responsibility |
| --- | --- |
| [`index.html`](index.html) | Page structure, model browser, map, command reference, and sign-in dialogs |
| [`app.js`](app.js) | Browser interactions, catalog filtering, and same-origin API requests |
| [`styles.css`](styles.css) | Responsive layout and visual styling |
| [`data/models.json`](data/models.json) | Public model catalog used by the browser |
| [`data/commands.json`](data/commands.json) | Public command reference used by the browser |
| [`assets/`](assets/) | Public images and map assets |
| [`../src/Sanctuary.WebAPI/`](../src/Sanctuary.WebAPI/) | Authentication, image storage, survey review, and statistics endpoints |
| [`../scripts/roleplay-model-guide/`](../scripts/roleplay-model-guide/) | Catalog and offline guide generation |
| [`../scripts/roleplay-vps/`](../scripts/roleplay-vps/) | Existing VPS packaging, static file installation, and reverse-proxy setup |

## Preview the static frontend

There is no Node build step or frontend package installation. With Python 3
installed, run this from the repository root:

```powershell
python -m http.server 8000 --bind 127.0.0.1 --directory site
```

Open `http://127.0.0.1:8000/`. Model browsing and the command reference use the
checked-in JSON files. Website sign-in, uploads, survey data, and statistics need
the WebAPI and will not work on a standalone static preview. The two downloadable
guides are copied into a `downloads/` directory by the VPS packaging script; a
plain preview does not create them.

For a complete instance, use [the server guide](../ROLEPLAY_SERVER.md). Do not
point a local development frontend at the production account database. The
website authentication cookie requires HTTPS; use a local HTTPS reverse proxy
for testing authenticated website features rather than weakening the production
cookie settings.

## How the frontend and API fit together

The production reverse proxy serves static files and routes API requests to the
WebAPI on the same origin. On the existing game host, the toolkit is mounted at
`/guide/`; the installer also contains a separate website-host configuration.
The current public working URL is `https://play.osfr-rp.com/guide/`.
The intended custom website host, `www.osfr-rp.com`, requires its public DNS and
HTTPS certificate to be working before it can serve as the primary link.

| API route | Purpose | Access boundary |
| --- | --- | --- |
| `GET /stats` | Aggregate account and character counts | Public; no individual account list |
| `GET /catalog/models` and `GET /catalog/models/{id}/image` | Preview metadata and images | Public |
| `GET /catalog/session` | Current website session | Reflects the current browser cookie |
| `POST /catalog/login` and `POST /catalog/logout` | Website session lifecycle | Login is rate-limited; logout requires a session |
| `POST /catalog/models/{id}/image` | Replace a model preview | Requires website authentication and upload validation |
| `GET /volunteers/placements/` | Survey and deployed-placement view | Public; includes contribution and review credits |
| `PUT` or `DELETE /volunteers/placements/{id}` | Edit or remove a survey submission | Authenticated; backend checks ownership, review status, and admin rights |

For the `/guide/` mount, the proxy strips that prefix from catalog requests and
maps `/guide/stats` to `/stats`. Survey requests use the root `/volunteers/` path.
These routes are implemented in
[`Endpoints/`](../src/Sanctuary.WebAPI/Endpoints/); authorization belongs in the
backend, not only in browser controls.

Website sign-in uses a separate encrypted cookie from the launcher's short-lived
session. Preview images are contributions, not secrets: uploaded images and
account credits are visible through the public catalog. Collection survey
positions, notes, character credits, and review credits are also public features.
Avoid entering private information in contributions or reproductions.

## What is safe to publish

Keep frontend code, public catalog JSON, generic server code, and reusable
configuration examples in Git. Do not copy a running server directory into this
folder: it may contain player data, upload metadata, authentication keys, logs,
and credentials. Generated guides may be distributed after checking their inputs;
production database exports and live contribution stores are not source assets.

Static assets are downloadable by visitors. Client-side code must never contain
an API secret or database password. Backend configuration and all persistent
instance data stay outside the web root and outside Git. Public source is not a
claim that the running service has received a complete security audit.

## Publishing website changes

Record user-visible changes in [the changelog](../CHANGELOG.md) and follow
[the publishing guide](../docs/RELEASING.md). A source push is separate from a
production deployment. The full VPS installer replaces the static site and
restarts the game services; it is not the appropriate path for a link-only edit.

For a website-only update, compare against the actual live files, preserve any
unpublished layout or map work, back up the touched files outside the public web
root, and replace only reviewed static files. Leave account storage, API
configuration, game resources, and service processes untouched. Verify both
public links and API-backed behavior appropriate to the change afterward.

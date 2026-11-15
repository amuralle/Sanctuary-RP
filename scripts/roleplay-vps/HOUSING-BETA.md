# RP Housing Beta

This merges Groaxy's housing foundation and our furniture editor into Sanctuary-RP.
It is for live multiplayer validation, not a claim that every housing interaction
is finished. The upstream contribution remains separate.

## Included

- Persistent house purchases and placed furniture, owner editing and pickup.
- Catalog mappings for 1,327 furniture definitions; existing positive coin prices
  are preserved. Unpriced furniture is not exposed as a new shop offer.
- Atomic catalog replacement, validated model mappings, transactional fixture
  edits, relevant-only fixture definitions, and static fixture entities.
- RP miniature commands, reconnect visibility fixes, saved overworld scenes,
  expanded collection drop tables and unlimited volunteer submissions preserved.

Miniature snapshots are currently keyed by zone definition. Private housing
instances deliberately do not restore or overwrite those overworld scenes.
Instance-scoped miniature persistence is separate work.

## Existing Host Update

Generate an idempotent EF migration script from this exact revision. Rehearse it
on a separate copy of the live database before running:

```powershell
./scripts/roleplay-vps/Deploy-RoleplayVps.ps1 -ExistingServer -SkipClientUpload -MigrationSql <rehearsed.sql> -StageOnly
```

StageOnly uploads the release and scripts without stopping services. After Linux
startup checks, invoke the staged Update-RoleplayRelease.sh as root, passing the
archive, release ID, migration SQL, and Recognize-LegacyCollectionMigration.sql.
That script stops services, backs up the DB, persistent files, settings and site,
checks existing table checksums, seeds missing hardpoint files, switches the
release symlink, and checks service health. It does not rewrite systemd, secrets,
Caddy, uploaded images, the website, or the hosted client.

The legacy collection migration recognition is RP-specific. It checks the old
table's contract before adding the upstream migration history ID; it never resets
collection credit. Rollback restores the previous code on activation failure.
Database changes are additive; restoring a DB backup is a separate, explicit
operator action, never an automatic overwrite of new player data.

## Client Fix

scripts/client-patches/fix_housing_browser_hud.py is the separately tested Lua
bytecode patch for the stale Use It Now browser HUD flag. Audit the input hash,
write a new file, back up the original and manifest, and regenerate the hosted
manifest before publishing it. It is intentionally not applied by the server
updater.

## Live Checks Still Required

Buy furniture, place/move/rotate/pick up, then relog. Verify purchase counts and
existing inventory. Test owner versus visitor editing with two accounts, and
verify persistence after a restart. Smoke-test mini spawn/move/speech/removal,
volunteer placements, and collection notifications after the upstream merge.
Interactive furniture actions and wallpaper/tint editing are not completed here.

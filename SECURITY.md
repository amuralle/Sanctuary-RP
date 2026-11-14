# Security policy

## Report privately

Use [GitHub's private vulnerability reporting form](https://github.com/amuralle/Sanctuary-RP/security/advisories/new)
for security issues affecting Sanctuary-RP or its companion website. Private
vulnerability reporting is enabled for this repository. Do not put exploitable
details, account credentials, private keys, or player data in public issues or
Discord channels.

Include the affected branch and commit, a concise impact description, and a
minimal reproduction using an instance and accounts you control. Redact logs and
screenshots. A maintainer can coordinate reproduction details and any disclosure
privately. For ordinary bugs, use
[GitHub issues](https://github.com/amuralle/Sanctuary-RP/issues); for community
questions, use [Discord](https://discord.gg/hrA85MU9Px).

## Scope and maintenance

Reports may concern the game services, WebAPI, website, or deployment scripts.
The public `main` and `roleplay/tabletop-controls` branches are development lines,
not a promise of long-term support or a guaranteed response time. Include the
running build's commit when known; public source may differ from the hosted build.

## Publishing boundary

Public source must not include production environment files, credentials,
SSH/TLS private keys, database dumps, player records, authentication/data-protection
keys, runtime logs, or backups. Keep persistent contribution stores outside Git
as well, even when selected contribution fields are intentionally public through
the API. Templates should use placeholders or generated secrets.

Before publishing, review the staged diff and archive contents. If a real secret
is exposed, revoke or rotate it first, then arrange repository-history cleanup;
a later deletion alone does not invalidate the leaked credential. Do not publish
an exploit as part of a routine changelog before a fix and disclosure plan are
ready. See [the publishing guide](docs/RELEASING.md).

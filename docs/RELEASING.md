# Publishing updates

[Project overview](../README.md) | [Change history](../CHANGELOG.md) | [Security policy](../SECURITY.md)

GitHub publication, a tagged software release, and a deployment to the hosted
server are separate actions. Publish the source and documentation you have
reviewed; deploy the specific build you intend to run. Record which commit was
used rather than treating the latest public commit as proof of deployment.

## Prepare a source update

1. Start from the intended RP branch in a clean checkout. Use `main` by default;
   target `roleplay/tabletop-controls` for work based on that development line.
   Keep main OSFR development and unrelated local experiments out of the update.
2. Review the exact staged files and their diff. Include reusable website and API
   source together when a feature needs both. Keep production configuration,
   credentials, databases, logs, backups, and live contribution stores out of Git.
3. Update [README.md](../README.md), [the command/setup guide](../ROLEPLAY_SERVER.md),
   or [the website guide](../site/README.md) when behavior changes. Add a concise
   changelog entry that separates implemented behavior, known limits, and
   operator actions.
4. Run the checks appropriate to the change. Documentation needs valid links and
   examples; frontend changes need a static preview and relevant browser checks.
   Authenticated interactions need a separate test instance. Server changes need
   the selected branch's SDK, a build, and relevant automated/integration tests.
   Report checks that were skipped or could not run.
5. Publish to the RP repository and verify the exact commit and changed files can
   be read without authentication. Check required CI results. Do not imply that a
   source push restarted or upgraded the hosted server.

The two public branches can differ in upstream history. Apply a shared
documentation correction to both branches explicitly rather than merging an
entire development branch just to synchronize a README.

## Write useful release notes

Identify the branch and commit, summarize changes for players and operators,
list migrations or configuration changes, and state the validation performed.
Use a version/tag only for an intentional software release. Label a prerelease
when appropriate and attach only artifacts produced from the identified commit.
Do not attach player databases, production environment files, or private keys.
Documentation-only publications do not need to pretend to be a new playable
server release.

## Update the website safely

Read [the website guide](../site/README.md) for static versus API-backed features.
For a small static update, compare with the live files first: the deployed toolkit
can contain unpublished changes. Back up only the files you will touch outside
the web root, record their hashes, and apply the reviewed change without copying
an older checkout over the whole site. Use a temporary file and atomic replacement
on the same filesystem, preserving ownership and permissions. Verify the public
HTTPS response and changed links after publishing. Keep a rollback copy.

A community-link update does not require account changes, database migrations,
game-resource changes, or a service restart. The existing full
[`Deploy-RoleplayVps.ps1`](../scripts/roleplay-vps/Deploy-RoleplayVps.ps1) and
[`Install-RoleplayRelease.sh`](../scripts/roleplay-vps/Install-RoleplayRelease.sh)
package all services, replace the website directory, and restart services. Review
their full effect before using them for an actual server release.

## Deploy a server release

Use a separate test instance first. Review migrations, back up the production
database and persistent resource/contribution stores, and confirm a restore path.
Keep host-specific settings outside the release archive. Schedule any required
restart, record the deployed commit and release directory, and check service
health, launcher connectivity, website login, uploads, and survey permissions
against the features changed. Automated tests and a healthy HTTP response do not
replace an in-game check when game behavior changes.

## Record the outcome

State separately what was published to GitHub, what was deployed, what was
verified, and what remains incomplete. If a hostname lacks DNS or a valid HTTPS
certificate, use a verified working URL in public navigation until that is fixed.

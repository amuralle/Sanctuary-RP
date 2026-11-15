#!/usr/bin/env bash
set -euo pipefail

# Update an already provisioned RP host without rewriting secrets, units, or Caddy.
if [[ $EUID -ne 0 || $# -ne 4 || ! $2 =~ ^[a-zA-Z0-9-]+$ ]]; then
    echo "Usage (root): $0 <archive> <release-id> <migrations.sql> <legacy-alias.sql>" >&2
    exit 2
fi
archive=$1
release="/opt/sanctuary/releases/$2"
migrations=$3
alias_sql=$4
previous=$(readlink -f /opt/sanctuary/current)
backup="/var/backups/sanctuary/pre-$2"
[[ -d $previous && ! -e $release && ! -e $backup ]]
[[ -f /etc/sanctuary/sanctuary.env && -d /var/lib/sanctuary/collection-node-spawns ]]
install -d -m 0750 -o sanctuary -g sanctuary "$release"
tar -xf "$archive" -C "$release"
chown -R sanctuary:sanctuary "$release"
chmod 0750 "$release"/{gateway/Sanctuary.Gateway,login/Sanctuary.Login,webapi/Sanctuary.WebAPI}
[[ -f $release/gateway/Resources/HousingFixtures.json ]]

install -d -m 0700 "$backup"
printf '%s\n' "$previous" > "$backup/previous-release.txt"
stopped=false
switched=false
recover() {
    if $stopped; then
        systemctl stop sanctuary-login sanctuary-gateway sanctuary-webapi || true
        if $switched; then
            ln -s "$previous" /opt/sanctuary/current.rollback
            mv -Tf /opt/sanctuary/current.rollback /opt/sanctuary/current
        fi
        # Migrations are additive. Never silently restore an older DB over player writes.
        systemctl start sanctuary-login sanctuary-gateway sanctuary-webapi || true
        echo "Activation failed; previous code restored. Backup: $backup" >&2
    fi
}
trap recover ERR
systemctl stop sanctuary-gateway sanctuary-login sanctuary-webapi
stopped=true
mariadb-dump --single-transaction --routines --events sanctuary_roleplay > "$backup/database.sql"
tar -czf "$backup/persistent-state.tar.gz" /var/lib/sanctuary /etc/sanctuary \
    /etc/systemd/system/sanctuary-*.service /etc/caddy/Caddyfile /srv/osfr-rp/site \
    "$previous/gateway/Resources" "$previous/login/Resources"
find /var/lib/sanctuary -type f ! -name '*.lock' -exec sha256sum {} + | sort > "$backup/files-before.sha256"
checksums='CHECKSUM TABLE Characters, Users, Items, CollectionEntries, Guilds, Mounts, Profiles, ProfileItems, Titles;'
mariadb -N sanctuary_roleplay -e "$checksums" > "$backup/db-before.txt"
mariadb sanctuary_roleplay < "$alias_sql"
mariadb sanctuary_roleplay < "$migrations"
mariadb -N sanctuary_roleplay -e "$checksums" > "$backup/db-after.txt"
diff -u "$backup/db-before.txt" "$backup/db-after.txt"
sha256sum -c "$backup/files-before.sha256" > "$backup/state-check.txt"

# Seed new groups only; never replace authored locations with repository samples.
while IFS= read -r -d '' source; do
    relative=${source#"$release/gateway/Resources/CollectionNodeSpawns/"}
    target="/var/lib/sanctuary/collection-node-spawns/$relative"
    if [[ ! -e $target ]]; then
        install -d -m 0750 -o sanctuary -g sanctuary "$(dirname "$target")"
        install -m 0640 -o sanctuary -g sanctuary "$source" "$target"
    fi
done < <(find "$release/gateway/Resources/CollectionNodeSpawns" -type f -name '*.json' -print0)
rm -rf -- "$release/gateway/Resources/CollectionNodeSpawns"
ln -s /var/lib/sanctuary/collection-node-spawns "$release/gateway/Resources/CollectionNodeSpawns"
ln -s /var/lib/sanctuary/collection-node-spawns "$release/webapi/CollectionNodeSpawns"
ln -s "$release" /opt/sanctuary/current.next
mv -Tf /opt/sanctuary/current.next /opt/sanctuary/current
switched=true
systemctl start sanctuary-login sanctuary-gateway sanctuary-webapi
for attempt in $(seq 1 60); do
    if curl -fsS http://127.0.0.1:20040/stats > "$backup/stats-after.json" &&
       systemctl is-active --quiet sanctuary-login sanctuary-gateway sanctuary-webapi; then
        sleep 5
        systemctl is-active --quiet sanctuary-login sanctuary-gateway sanctuary-webapi
        stopped=false
        trap - ERR
        echo "Activated $release; complete pre-update backup: $backup"
        exit 0
    fi
    sleep 2
done
false

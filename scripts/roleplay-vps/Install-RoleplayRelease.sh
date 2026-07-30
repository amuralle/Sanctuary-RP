#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 3 ]]; then
    echo "Usage: $0 <release-archive> <release-id> <domain>" >&2
    exit 2
fi

archive_path=$1
release_id=$2
domain=$3
release_path="/opt/sanctuary/releases/$release_id"
environment_path=/etc/sanctuary/sanctuary.env
backup_client_path=/etc/sanctuary/backup.cnf

if [[ ! $domain =~ ^[a-zA-Z0-9.-]+$ ]]; then
    echo "Invalid deployment domain: $domain" >&2
    exit 2
fi

if [[ $EUID -ne 0 ]]; then
    echo "This installer must run as root." >&2
    exit 1
fi

if ! command -v xxhsum >/dev/null; then
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install -y xxhash
fi

install -d -m 0750 -o sanctuary -g sanctuary "$release_path"
tar -xf "$archive_path" -C "$release_path"
chown -R sanctuary:sanctuary "$release_path"
chmod 0750 \
    "$release_path/gateway/Sanctuary.Gateway" \
    "$release_path/login/Sanctuary.Login" \
    "$release_path/webapi/Sanctuary.WebAPI"

install -d -m 0750 -o root -g sanctuary /etc/sanctuary

if [[ ! -f $environment_path ]]; then
    database_password=$(openssl rand -hex 32)
    gateway_challenge=$(openssl rand -hex 32)
    database_version=$(mariadb --silent --skip-column-names -e "SELECT VERSION()" |
        sed -E 's/^([0-9]+\.[0-9]+\.[0-9]+).*/\1-mariadb/')

    cat > "$environment_path" <<EOF
DOTNET_ENVIRONMENT=Production
Database__Provider=0
Database__VersionString=$database_version
Database__ConnectionString=Server=127.0.0.1;Port=3306;Database=sanctuary_roleplay;User=sanctuary;Password=$database_password;
Server__LoginGatewayChallenge=$gateway_challenge
WebAPI__LaunchArguments="AssetDelivery:IndirectServerAddress=http://osfr.editz.dev/assets"
WebAPI__MemberByDefault=true
EOF

    chmod 0640 "$environment_path"
    chown root:sanctuary "$environment_path"

fi

database_password=$(sed -nE 's/^Database__ConnectionString=.*Password=([^;]+);/\1/p' "$environment_path")

if [[ -z $database_password ]]; then
    echo "Database password is missing from $environment_path" >&2
    exit 1
fi

mariadb <<EOF
CREATE DATABASE IF NOT EXISTS sanctuary_roleplay CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'sanctuary'@'127.0.0.1' IDENTIFIED BY '$database_password';
ALTER USER 'sanctuary'@'127.0.0.1' IDENTIFIED BY '$database_password';
GRANT ALL PRIVILEGES ON sanctuary_roleplay.* TO 'sanctuary'@'127.0.0.1';
FLUSH PRIVILEGES;
EOF

ln -sfn "$release_path" /opt/sanctuary/current

install -d -m 0755 -o caddy -g caddy /srv/osfr-rp/www
cat > /srv/osfr-rp/www/servermanifest.xml <<EOF
<?xml version="1.0" encoding="utf-8"?>
<ServerManifest version="2">
  <Name>OSFR Roleplay</Name>
  <Description>A small Free Realms roleplay and virtual tabletop server.</Description>
  <WebApiUrl>https://$domain</WebApiUrl>
  <LoginServer>$domain:20042</LoginServer>
</ServerManifest>
EOF
chown caddy:caddy /srv/osfr-rp/www/servermanifest.xml

cat > /etc/systemd/system/sanctuary-login.service <<'EOF'
[Unit]
Description=Sanctuary login server
After=network-online.target mariadb.service
Wants=network-online.target
Requires=mariadb.service

[Service]
Type=simple
User=sanctuary
Group=sanctuary
WorkingDirectory=/opt/sanctuary/current/login
EnvironmentFile=/etc/sanctuary/sanctuary.env
Environment=Server__Port=20042
Environment=Server__LoginGatewayPort=20041
ExecStart=/opt/sanctuary/current/login/Sanctuary.Login
Restart=on-failure
RestartSec=5
TimeoutStopSec=20
UMask=0027
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/opt/sanctuary /var/lib/sanctuary /var/log/sanctuary
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6

[Install]
WantedBy=multi-user.target
EOF

cat > /etc/systemd/system/sanctuary-gateway.service <<EOF
[Unit]
Description=Sanctuary gateway server
After=network-online.target mariadb.service sanctuary-login.service
Wants=network-online.target
Requires=mariadb.service sanctuary-login.service

[Service]
Type=simple
User=sanctuary
Group=sanctuary
WorkingDirectory=/opt/sanctuary/current/gateway
EnvironmentFile=/etc/sanctuary/sanctuary.env
Environment=Server__Port=20260
Environment=Server__Environment=live
Environment=Server__ClientVersion=1.910.1.530630
Environment=Server__ServerAddress=$domain:20260
Environment=Server__LoginGatewayAddress=127.0.0.1:20041
Environment=Server__ShowMemberNagScreen=false
ExecStart=/opt/sanctuary/current/gateway/Sanctuary.Gateway
Restart=on-failure
RestartSec=5
TimeoutStopSec=20
UMask=0027
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/opt/sanctuary /var/lib/sanctuary /var/log/sanctuary
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6

[Install]
WantedBy=multi-user.target
EOF

cat > /etc/systemd/system/sanctuary-webapi.service <<'EOF'
[Unit]
Description=Sanctuary web API
After=network-online.target mariadb.service
Wants=network-online.target
Requires=mariadb.service

[Service]
Type=simple
User=sanctuary
Group=sanctuary
WorkingDirectory=/opt/sanctuary/current/webapi
EnvironmentFile=/etc/sanctuary/sanctuary.env
Environment=ASPNETCORE_URLS=http://127.0.0.1:20040
ExecStart=/opt/sanctuary/current/webapi/Sanctuary.WebAPI
Restart=on-failure
RestartSec=5
TimeoutStopSec=20
UMask=0027
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/opt/sanctuary /var/lib/sanctuary /var/log/sanctuary
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6

[Install]
WantedBy=multi-user.target
EOF

cat > /etc/caddy/Caddyfile <<EOF
$domain {
    encode zstd gzip

    route {
        @portrait path /image
        respond @portrait 404

        @auth path /login /register
        reverse_proxy @auth 127.0.0.1:20040

        root * /srv/osfr-rp/www
        file_server
    }

    header {
        X-Content-Type-Options nosniff
        Referrer-Policy no-referrer
        -Server
    }
}
EOF
caddy fmt --overwrite /etc/caddy/Caddyfile

cat > "$backup_client_path" <<EOF
[client]
host=127.0.0.1
user=sanctuary
password=$database_password
EOF
chmod 0600 "$backup_client_path"

cat > /usr/local/sbin/backup-sanctuary <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

backup_root=/var/backups/sanctuary
stamp=$(date -u +%Y%m%dT%H%M%SZ)
work_path=$(mktemp -d)
trap 'rm -rf "$work_path"' EXIT

mariadb-dump --defaults-extra-file=/etc/sanctuary/backup.cnf \
    --single-transaction --routines --events sanctuary_roleplay > "$work_path/database.sql"

if [[ -d /opt/sanctuary/current/gateway/Resources ]]; then
    cp -a /opt/sanctuary/current/gateway/Resources "$work_path/Resources"
fi

tar -czf "$backup_root/sanctuary-$stamp.tar.gz" -C "$work_path" .
chown root:root "$backup_root/sanctuary-$stamp.tar.gz"
chmod 0600 "$backup_root/sanctuary-$stamp.tar.gz"
find "$backup_root" -type f -name 'sanctuary-*.tar.gz' -mtime +14 -delete
EOF
chmod 0750 /usr/local/sbin/backup-sanctuary

cat > /etc/systemd/system/sanctuary-backup.service <<'EOF'
[Unit]
Description=Back up the Sanctuary roleplay server
After=mariadb.service
Requires=mariadb.service

[Service]
Type=oneshot
ExecStart=/usr/local/sbin/backup-sanctuary
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/var/backups/sanctuary
EOF

cat > /etc/systemd/system/sanctuary-backup.timer <<'EOF'
[Unit]
Description=Daily Sanctuary roleplay server backup

[Timer]
OnCalendar=*-*-* 04:17:00 UTC
Persistent=true
RandomizedDelaySec=15m

[Install]
WantedBy=timers.target
EOF

systemctl daemon-reload
systemctl enable sanctuary-login sanctuary-gateway sanctuary-webapi sanctuary-backup.timer
caddy validate --config /etc/caddy/Caddyfile
systemctl restart sanctuary-login sanctuary-gateway sanctuary-webapi caddy
systemctl restart sanctuary-backup.timer

mapfile -t old_releases < <(
    find /opt/sanctuary/releases -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' |
        sort -nr |
        cut -d' ' -f2- |
        tail -n +4
)

for old_release in "${old_releases[@]}"; do
    rm -rf "$old_release"
done

rm -f "$archive_path"

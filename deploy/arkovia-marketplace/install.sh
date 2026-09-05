#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID:-$(id -u)} -ne 0 ]]; then
  echo "Run this installer as root (for example: sudo bash deploy/arkovia-marketplace/install.sh)." >&2
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SERVICE_NAME="arkovia-marketplace"
SERVICE_USER="arkovia-market"
SERVICE_GROUP="arkovia-market"
INSTALL_DIR="/opt/arkovia/marketplace"
STAGE_DIR="/opt/arkovia/.marketplace-stage"
ENV_DIR="/etc/arkovia"
ENV_FILE="$ENV_DIR/marketplace.env"
UNIT_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
NGINX_SNIPPET="/etc/nginx/snippets/arkovia-marketplace.conf"

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Required command '$1' is not installed." >&2
    exit 1
  }
}

require_command dotnet
require_command systemctl
require_command install

if command -v nginx >/dev/null 2>&1; then
  HAVE_NGINX=1
else
  HAVE_NGINX=0
fi

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  useradd --system --home-dir "$INSTALL_DIR" --shell /usr/sbin/nologin "$SERVICE_USER"
fi

install -d -m 0755 -o root -g root /opt/arkovia
rm -rf "$STAGE_DIR"
install -d -m 0755 -o root -g root "$STAGE_DIR"

echo "Publishing Arkovia Marketplace..."
dotnet publish "$REPO_ROOT/services/ArkoviaMarketplace/ArkoviaMarketplace.csproj" \
  -c Release \
  -o "$STAGE_DIR"

install -d -m 0700 -o root -g root "$ENV_DIR"

if [[ ! -f "$ENV_FILE" ]]; then
  echo
  echo "First-time configuration. The TShock REST token will not be echoed."
  read -r -s -p "TShock REST token: " TSHOCK_TOKEN
  echo
  if [[ -z "$TSHOCK_TOKEN" ]]; then
    echo "TShock REST token is required." >&2
    rm -rf "$STAGE_DIR"
    exit 1
  fi

  SUBJECT_SECRET="$(openssl rand -hex 48 2>/dev/null || true)"
  if [[ ${#SUBJECT_SECRET} -lt 32 ]]; then
    SUBJECT_SECRET="$(python3 - <<'PY'
import secrets
print(secrets.token_hex(48))
PY
)"
  fi

  umask 077
  cat > "$ENV_FILE" <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5080
ARKOVIA_TSHOCK_REST_URL=http://127.0.0.1:7878
ARKOVIA_TSHOCK_REST_TOKEN=$TSHOCK_TOKEN
ARKOVIA_MARKET_SUBJECT_SECRET=$SUBJECT_SECRET
ARKOVIA_MARKET_COOKIE_SECURE=true
EOF
  chmod 0600 "$ENV_FILE"
  chown root:root "$ENV_FILE"
  unset TSHOCK_TOKEN SUBJECT_SECRET
  echo "Created $ENV_FILE with a persistent generated subject secret."
else
  chmod 0600 "$ENV_FILE"
  chown root:root "$ENV_FILE"
  echo "Preserving existing $ENV_FILE and its marketplace subject secret."
fi

# Validate that required environment variables are present without printing their values.
for name in ARKOVIA_TSHOCK_REST_TOKEN ARKOVIA_MARKET_SUBJECT_SECRET; do
  if ! grep -q "^${name}=." "$ENV_FILE"; then
    echo "$ENV_FILE is missing a non-empty $name value." >&2
    rm -rf "$STAGE_DIR"
    exit 1
  fi
done

if [[ -d "$INSTALL_DIR" ]]; then
  rm -rf "${INSTALL_DIR}.previous"
  mv "$INSTALL_DIR" "${INSTALL_DIR}.previous"
fi
mv "$STAGE_DIR" "$INSTALL_DIR"
chown -R root:root "$INSTALL_DIR"
find "$INSTALL_DIR" -type d -exec chmod 0755 {} +
find "$INSTALL_DIR" -type f -exec chmod 0644 {} +

install -m 0644 -o root -g root \
  "$REPO_ROOT/deploy/arkovia-marketplace/arkovia-marketplace.service" \
  "$UNIT_FILE"

if [[ $HAVE_NGINX -eq 1 ]]; then
  install -d -m 0755 -o root -g root /etc/nginx/snippets
  install -m 0644 -o root -g root \
    "$REPO_ROOT/deploy/arkovia-marketplace/nginx-location.conf" \
    "$NGINX_SNIPPET"
fi

systemctl daemon-reload
systemctl enable "$SERVICE_NAME"
systemctl restart "$SERVICE_NAME"

for _ in {1..20}; do
  if curl -fsS --max-time 2 http://127.0.0.1:5080/healthz >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

if ! curl -fsS --max-time 3 http://127.0.0.1:5080/healthz >/dev/null; then
  echo "Marketplace process did not become healthy. Recent service log:" >&2
  journalctl -u "$SERVICE_NAME" -n 50 --no-pager >&2 || true
  exit 1
fi

echo "Marketplace process is healthy on http://127.0.0.1:5080/healthz"

if curl -fsS --max-time 5 http://127.0.0.1:5080/readyz >/dev/null 2>&1; then
  echo "Marketplace is ready and can reach the private TShock marketplace API."
else
  echo "WARNING: /readyz is not healthy. Check the TShock REST token, permissions, REST listener, and plugin restart." >&2
fi

if [[ $HAVE_NGINX -eq 1 ]]; then
  echo "Installed Nginx snippet: $NGINX_SNIPPET"
  echo "Add this inside the existing HTTPS server block for arkovia-node1.mywire.org:"
  echo "  include $NGINX_SNIPPET;"
  echo "Then run: nginx -t && systemctl reload nginx"
else
  echo "Nginx was not detected; install/configure your HTTPS reverse proxy separately."
fi

echo "Deployment files installed successfully."

#!/bin/bash
set -euo pipefail

CERT_DIR="${POSTGRES_SSL_CERT_DIR:-/var/lib/postgresql/certs}"
mkdir -p "${CERT_DIR}"

if [[ ! -f "${CERT_DIR}/server.key" || ! -f "${CERT_DIR}/server.crt" ]]; then
  openssl req -new -x509 -days 3650 -nodes \
    -out "${CERT_DIR}/server.crt" \
    -keyout "${CERT_DIR}/server.key" \
    -subj "/CN=medicalmanager-db"
fi

chmod 600 "${CERT_DIR}/server.key"
chmod 644 "${CERT_DIR}/server.crt"
chown postgres:postgres "${CERT_DIR}/server.key" "${CERT_DIR}/server.crt"

exec docker-entrypoint.sh "$@"

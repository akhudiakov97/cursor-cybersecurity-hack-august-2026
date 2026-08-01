#!/usr/bin/env bash
# HoneyGuard 3-minute demo script: the "attacker terminal" side of the split screen.
#
# Run this while the app is running (dotnet run) and the dashboard is open in a browser.
# Each run uses a freshly random spoofed attacker IP (via X-Forwarded-For, honored only
# in Development - see HoneyGuardOptions.TrustForwardedForHeader) so you can re-run this
# script repeatedly without banning the same "attacker" twice.
#
# Usage: ./demo/attack.sh [base-url]
set -euo pipefail

BASE_URL="${1:-http://localhost:5245}"
ATTACKER_IP="203.0.113.$((RANDOM % 200 + 10))"

print_step() {
  echo
  echo "=== $1 ==="
}

request() {
  curl -sS -o /dev/null -w "%{http_code}" -H "X-Forwarded-For: ${ATTACKER_IP}" "$@"
}

echo "Simulating attacker at spoofed IP ${ATTACKER_IP} against ${BASE_URL}"

print_step "1. Normal request to /api/v1/products (expect 200)"
status=$(request "${BASE_URL}/api/v1/products")
echo "-> HTTP ${status}"

print_step "2. Probing honeypot trap /api/v1/admin/config (expect 404, but the IP is now banned)"
status=$(request "${BASE_URL}/api/v1/admin/config")
echo "-> HTTP ${status}"

print_step "3. Trying a real request again (expect 403 - instantly blocked)"
status=$(request "${BASE_URL}/api/v1/products")
echo "-> HTTP ${status}"

echo
echo "Check the HoneyGuard dashboard - it should already show this incident, no refresh needed."

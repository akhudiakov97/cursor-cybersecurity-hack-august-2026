#!/usr/bin/env bash
# One-shot setup for HoneyGuard's Railway service variables.
#
# This does NOT create the Railway project for you - Railway has no way to accept a
# repo connection non-interactively (it requires you to authorize GitHub access in the
# browser). Create the project first (railway.app -> New Project -> Deploy from GitHub
# repo -> pick this repo), then run this script to fill in every service variable in one
# shot instead of clicking through the dashboard one field at a time.
#
# Usage:
#   ./scripts/deploy-railway.sh
#
# The Supabase service_role key is read with a hidden prompt (via `read -s`) so it never
# ends up in your shell history or in this script - it is only ever sent straight to
# Railway's `variables --set` call.
set -euo pipefail

if ! command -v railway &> /dev/null; then
  echo "Railway CLI not found - installing it with npm..."
  npm install -g @railway/cli
fi

if ! railway whoami &> /dev/null; then
  echo "Not logged in to Railway - opening browser to authenticate..."
  railway login
fi

if [ ! -f .railway/config.json ] && [ -z "${RAILWAY_PROJECT_ID:-}" ]; then
  echo "Linking this directory to a Railway project/service..."
  railway link
fi

read -rsp "Supabase service_role key (input hidden): " SUPABASE_SERVICE_ROLE_KEY
echo

railway variables \
  --set "HoneyGuard__SupabaseServiceRoleKey=${SUPABASE_SERVICE_ROLE_KEY}" \
  --set "HoneyGuard__DemoMode=true" \
  --set "HoneyGuard__TrustForwardedForHeader=true" \
  --set "HoneyGuard__BanDuration=00:02:00" \
  --set "ASPNETCORE_ENVIRONMENT=Production"

echo "Service variables set. Trigger a deploy with: railway up"

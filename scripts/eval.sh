#!/usr/bin/env bash
# Runs the promptfoo eval harness against the running backend.
# Handles Node version, dependency install, and backend health check so
# the reviewer just runs:
#   ./scripts/eval.sh          # run eval and print summary
#   ./scripts/eval.sh view     # open the HTML dashboard after a run
#
# Requires .NET backend running on :5181 (./scripts/dev.sh).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EVALS_DIR="$REPO_ROOT/evals"

# --- Node LTS activation (same logic as dev.sh — duplicated on purpose so
# this script works standalone, without depending on dev.sh having been sourced).
TARGET_MAJOR="22"
[ -f "$REPO_ROOT/.nvmrc" ] && TARGET_MAJOR="$(tr -d ' \n\r' < "$REPO_ROOT/.nvmrc")"

if command -v fnm >/dev/null 2>&1; then
  eval "$(fnm env 2>/dev/null || true)"
  fnm install "$TARGET_MAJOR" >/dev/null 2>&1 || true
  fnm use "$TARGET_MAJOR" >/dev/null 2>&1 || true
elif [ -s "$HOME/.nvm/nvm.sh" ]; then
  # shellcheck disable=SC1091
  . "$HOME/.nvm/nvm.sh"
  nvm install "$TARGET_MAJOR" >/dev/null 2>&1 || true
  nvm use "$TARGET_MAJOR" >/dev/null 2>&1 || true
fi

if ! command -v node >/dev/null 2>&1; then
  echo "ERROR: Node not found. Install Node $TARGET_MAJOR LTS: https://nodejs.org/" >&2
  exit 1
fi

CURRENT_MAJOR="$(node --version | sed 's/^v//' | cut -d. -f1)"
if [ "$CURRENT_MAJOR" != "$TARGET_MAJOR" ]; then
  echo "WARNING: Node v$CURRENT_MAJOR is active, expected v$TARGET_MAJOR LTS (from .nvmrc)." >&2
  echo "         Eval will attempt to continue, but better-sqlite3 may fail." >&2
fi

# --- Backend health check
if ! curl -sSf http://localhost:5181/api/health >/dev/null 2>&1; then
  echo "ERROR: backend not reachable at http://localhost:5181/api/health" >&2
  echo "       Start it with: ./scripts/dev.sh" >&2
  exit 1
fi

# --- First-run install. Keep node_modules local to evals/ so the version
# binds to the active Node (avoids npx cache NODE_MODULE_VERSION mismatches).
cd "$EVALS_DIR"
if [ ! -d node_modules ]; then
  echo "→ installing promptfoo (first run)…"
  if command -v bun >/dev/null 2>&1; then
    # bun is fine for installing but promptfoo must run on Node — use npm install
    # to keep the native bindings consistent with the active Node.
    npm install --no-audit --no-fund
  else
    npm install --no-audit --no-fund
  fi
fi

case "${1:-eval}" in
  eval)
    echo "→ running eval (15 cases)…"
    # Threshold gate: fail the run if we drop below 80% pass rate.
    npx promptfoo eval --no-progress-bar
    ;;
  view)
    npx promptfoo view
    ;;
  *)
    echo "usage: $0 [eval|view]" >&2
    exit 64
    ;;
esac

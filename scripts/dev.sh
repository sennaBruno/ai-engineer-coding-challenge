#!/usr/bin/env bash
# Seamless dev launcher for the Grocery SOP Assistant POC.
# Starts the .NET 10 API on :5181 and the Vite frontend on :5173.
#
# Usage:
#   ./scripts/dev.sh            # start both, log to scripts/logs/
#   ./scripts/dev.sh stop       # stop both
#   ./scripts/dev.sh logs       # tail both logs
#
# OpenAI key resolution (first match wins):
#   1. $OPENAI_API_KEY env var already exported
#   2. .local/openai-key (gitignored local file — put the key on a single line)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOGS_DIR="$REPO_ROOT/scripts/logs"
BACKEND_LOG="$LOGS_DIR/backend.log"
FRONTEND_LOG="$LOGS_DIR/frontend.log"
BACKEND_PID_FILE="$LOGS_DIR/backend.pid"
FRONTEND_PID_FILE="$LOGS_DIR/frontend.pid"

mkdir -p "$LOGS_DIR"

resolve_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then
    echo "dotnet"; return
  fi
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    echo "$HOME/.dotnet/dotnet"; return
  fi
  echo ""
}

resolve_key() {
  if [ -n "${OPENAI_API_KEY:-}" ]; then
    return
  fi
  if [ -f "$REPO_ROOT/.local/openai-key" ]; then
    OPENAI_API_KEY="$(tr -d '\n\r' < "$REPO_ROOT/.local/openai-key")"
    export OPENAI_API_KEY
  fi
}

stop_one() {
  local pid_file="$1"
  [ -f "$pid_file" ] || return 0
  local pid
  pid="$(cat "$pid_file")"
  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    # give it a beat, then SIGKILL if still alive
    sleep 1
    kill -9 "$pid" 2>/dev/null || true
  fi
  rm -f "$pid_file"
}

case "${1:-start}" in
  stop)
    stop_one "$BACKEND_PID_FILE"
    stop_one "$FRONTEND_PID_FILE"
    echo "stopped"
    exit 0
    ;;
  logs)
    exec tail -f "$BACKEND_LOG" "$FRONTEND_LOG"
    ;;
  start|"")
    ;;
  *)
    echo "usage: $0 [start|stop|logs]" >&2
    exit 64
    ;;
esac

DOTNET="$(resolve_dotnet)"
if [ -z "$DOTNET" ]; then
  echo "ERROR: .NET SDK not found on PATH or at \$HOME/.dotnet/dotnet." >&2
  echo "Install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 1
fi

resolve_key
if [ -z "${OPENAI_API_KEY:-}" ]; then
  echo "ERROR: OPENAI_API_KEY not set." >&2
  echo "Either:" >&2
  echo "  export OPENAI_API_KEY=sk-..." >&2
  echo "  or write it to $REPO_ROOT/.local/openai-key (gitignored)." >&2
  exit 1
fi

# Bun for the frontend. Fall back to npm/pnpm if the dev uses those.
FRONTEND_PM="bun"
if ! command -v bun >/dev/null 2>&1; then
  if command -v pnpm >/dev/null 2>&1; then FRONTEND_PM="pnpm"
  elif command -v npm >/dev/null 2>&1; then FRONTEND_PM="npm"
  else
    echo "ERROR: no bun/pnpm/npm found for the frontend." >&2
    exit 1
  fi
fi

# Stop any prior run before starting new ones, so re-running dev.sh is idempotent.
stop_one "$BACKEND_PID_FILE"
stop_one "$FRONTEND_PID_FILE"

echo "→ starting backend (http://localhost:5181)"
(
  cd "$REPO_ROOT/backend/src/Api"
  export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
  export ASPNETCORE_ENVIRONMENT=Development
  # Key is inherited from the parent environment, never echoed to ps.
  "$DOTNET" run --urls http://localhost:5181 --no-launch-profile
) > "$BACKEND_LOG" 2>&1 &
echo $! > "$BACKEND_PID_FILE"

echo "→ starting frontend (Vite picks the first free port in 5173..5176)"
(
  cd "$REPO_ROOT/frontend"
  if [ ! -d node_modules ]; then
    echo "[dev.sh] installing frontend deps..."
    case "$FRONTEND_PM" in
      bun) bun install ;;
      pnpm) pnpm install ;;
      npm) npm install ;;
    esac
  fi
  # No --strictPort: if 5173 is taken by another project, Vite hops to 5174.
  # The backend CORS allowlist covers 5173–5176.
  case "$FRONTEND_PM" in
    bun) exec bun run dev ;;
    pnpm) exec pnpm dev ;;
    npm) exec npm run dev ;;
  esac
) > "$FRONTEND_LOG" 2>&1 &
echo $! > "$FRONTEND_PID_FILE"

# Wait up to 30 s for backend health + frontend. Parse the port Vite chose from
# its own log rather than probing ports — avoids picking up another project
# that happens to be listening on 5173.
detect_frontend_port() {
  grep -oE "http://localhost:[0-9]+" "$FRONTEND_LOG" 2>/dev/null \
    | grep -oE "[0-9]+$" \
    | head -1
}

echo -n "→ waiting for services"
FRONTEND_PORT=""
for i in $(seq 1 30); do
  if curl -sSf http://localhost:5181/api/health >/dev/null 2>&1; then
    FRONTEND_PORT="$(detect_frontend_port)"
    if [ -n "$FRONTEND_PORT" ] && curl -sSf "http://localhost:$FRONTEND_PORT" >/dev/null 2>&1; then
      echo " up"
      break
    fi
  fi
  echo -n "."
  sleep 1
done
echo

if [ -z "$FRONTEND_PORT" ]; then
  FRONTEND_PORT="(check $FRONTEND_LOG)"
fi

cat <<EOF
────────────────────────────────────────────────────
  Backend:  http://localhost:5181/api/health
  Frontend: http://localhost:$FRONTEND_PORT
  Logs:     $LOGS_DIR/*.log  (./scripts/dev.sh logs)
  Stop:     ./scripts/dev.sh stop
────────────────────────────────────────────────────
EOF

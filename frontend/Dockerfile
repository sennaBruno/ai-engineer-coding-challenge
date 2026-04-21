# syntax=docker/dockerfile:1.7

# ---------- Stage 1: install deps + build the Vite bundle ----------
#
# Bun handles install + build. Manifests first so restore only re-runs
# when dependencies actually change.
FROM oven/bun:1-alpine AS build
WORKDIR /app

COPY frontend/package.json frontend/bun.lock ./
RUN bun install --frozen-lockfile

COPY frontend/ ./

# Vite bakes VITE_API_BASE_URL into the bundle at build time. The
# browser, not the container, calls the backend — so the URL must be
# reachable from the host. Compose passes this as a build arg; override
# with --build-arg when deploying to a real backend host.
ARG VITE_API_BASE_URL=http://localhost:5181
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN bun run build


# ---------- Stage 2: minimal runtime, serve dist/ via vite preview ----
#
# No nginx. `vite preview` is Vite's built-in static server, ships with
# the package we already installed, and serves the built bundle with
# proper SPA fallback. Smaller surface than a full HTTP server, plenty
# for a local-first POC.
FROM oven/bun:1-alpine AS runtime
WORKDIR /app

# Only what preview needs at runtime: the built bundle, vite itself, and
# vite.config.ts so any config-level rewrites stay consistent with what
# the dev server would do.
COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json ./package.json
COPY --from=build /app/vite.config.ts ./vite.config.ts

# Non-root user (Alpine busybox adduser syntax).
RUN addgroup -S app && adduser -S app -G app \
    && chown -R app:app /app

USER app

EXPOSE 5173

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -qO- http://127.0.0.1:5173/ > /dev/null || exit 1
    # Use 127.0.0.1 explicitly: alpine's resolver prefers IPv6 for
    # `localhost` on some configs, and vite preview binds IPv4-only.
    # -qO- fetches GET rather than --spider (HEAD), which some static
    # servers reject.

# Invoke vite directly from node_modules to avoid any run-script
# indirection. --host 0.0.0.0 so the container's port is reachable from
# the host; default is 127.0.0.1 which only listens inside the container.
CMD ["./node_modules/.bin/vite", "preview", "--host", "0.0.0.0", "--port", "5173"]

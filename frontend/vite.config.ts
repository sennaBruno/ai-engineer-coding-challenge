import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
  },
  preview: {
    // Vite 5+ `preview` enforces a Host-header allowlist. With
    // `--host 0.0.0.0` in the Docker runtime, a reviewer running the
    // container behind a VM, remote Docker host, or WSL backend can end
    // up with a Host header that isn't localhost — which would surface
    // as a "blocked request" 403. Trusting any host is fine here since
    // no cookies or credentialed CORS are involved.
    allowedHosts: true,
  },
})

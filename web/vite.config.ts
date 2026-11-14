import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig, type Plugin } from 'vite'

// One id per build: in the code as __BUILD_ID__ and in /version.json, so an open tab can tell a new deploy is out.
const buildId = process.env.CF_PAGES_COMMIT_SHA ?? process.env.GITHUB_SHA ?? new Date().toISOString()
const versionFile = (): Plugin => ({
  name: 'polle-version-file', apply: 'build',
  generateBundle() { this.emitFile({ type: 'asset', fileName: 'version.json', source: JSON.stringify({ build: buildId }) }) },
})

export default defineConfig(({ command }) => ({
  plugins: [react(), tailwindcss(), versionFile()],
  define: { __BUILD_ID__: JSON.stringify(command === 'build' ? buildId : 'dev') },
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    host: 'localhost',
    port: 5173,
    strictPort: true,
    // Same-origin /api in dev and in Docker (nginx) — no CORS needed.
    proxy: { '/api': process.env.API_URL ?? 'http://localhost:5080' },
  },
  preview: {
    host: 'localhost',
    port: 4173,
    strictPort: true,
    proxy: { '/api': process.env.API_URL ?? 'http://localhost:5080' },
  },
}))

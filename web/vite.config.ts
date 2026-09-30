/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { proxy: { '/api': { target: process.env.API_URL ?? 'http://localhost:5080', changeOrigin: true } } },
  test: { environment: 'jsdom', globals: true },
})

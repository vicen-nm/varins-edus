import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

// The API runs on HTTPS with a development certificate: "secure: false" accepts it.
// The browser (or the phone) only talks to Vite, so the API is "same origin" as in production.
const apiProxy = {
  '/api': { target: 'https://localhost:7172', secure: false },
  '/health': { target: 'https://localhost:7172', secure: false },
};

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      // A new version asks the user before reloading, so nobody loses what they were typing.
      registerType: 'prompt',
      includeAssets: ['favicon.ico', 'apple-touch-icon-180x180.png', 'logo.svg'],
      manifest: {
        id: '/',
        name: 'VarinsEdu',
        short_name: 'VarinsEdu',
        description: 'Plataforma de gestión para instituciones educativas.',
        lang: 'es',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        background_color: '#0b1b2b',
        theme_color: '#0b1b2b',
        icons: [
          { src: 'pwa-64x64.png', sizes: '64x64', type: 'image/png' },
          { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' },
          { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png' },
          {
            src: 'maskable-icon-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      workbox: {
        // Only the app itself is cached. API responses are never cached.
        globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/health/, /^\/scalar/, /^\/openapi/],
        cleanupOutdatedCaches: true,
      },
    }),
  ],
  server: {
    port: 5173,
    strictPort: true,
    proxy: apiProxy,
  },
  // Same port as the dev server.
  preview: {
    port: 5173,
    strictPort: true,
    proxy: apiProxy,
  },
  test: {
    include: ['src/**/*.test.ts'],
  },
});
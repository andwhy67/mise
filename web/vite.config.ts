/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

const apiTarget = process.env.VITE_DEV_API_PROXY ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [
    react(),
    // PWA (DESIGN.md §8): свой service worker (пуши, оффлайн-оболочка), обновление — по согласию пользователя.
    VitePWA({
      strategies: 'injectManifest',
      srcDir: 'src/sw',
      filename: 'service-worker.ts',
      registerType: 'prompt',
      injectRegister: false,
      injectManifest: {
        globPatterns: ['**/*.{js,css,html,woff2,png,svg}'],
        // Чанк «Отчётов» с Recharts тоже кэшируется: открыть отчёт без сети — нормальный сценарий.
        maximumFileSizeToCacheInBytes: 3 * 1024 * 1024,
      },
      manifest: {
        id: '/',
        name: 'Mise',
        short_name: 'Mise',
        description: 'Остатки, сроки годности и стоп-лист ресторана в реальном времени',
        lang: 'ru',
        dir: 'ltr',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        orientation: 'any',
        theme_color: '#0077B8',
        background_color: '#F2F7FB',
        categories: ['business', 'food', 'productivity'],
        icons: [
          { src: '/pwa-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
          { src: '/pwa-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
          { src: '/pwa-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
        // Долгое нажатие на иконку: самые частые действия смены.
        shortcuts: [
          { name: 'Принять заказ', short_name: 'Заказ', url: '/orders', icons: [{ src: '/pwa-192.png', sizes: '192x192' }] },
          { name: 'Принять поставку', short_name: 'Поставка', url: '/supplies?receive=1', icons: [{ src: '/pwa-192.png', sizes: '192x192' }] },
          { name: 'Стоп-лист', short_name: 'Стоп', url: '/stoplist', icons: [{ src: '/pwa-192.png', sizes: '192x192' }] },
        ],
      },
      // В dev service worker не нужен: мешает горячей перезагрузке и съедает память.
      devOptions: { enabled: false },
    }),
  ],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': apiTarget,
      '/health': apiTarget,
      '/hubs': { target: apiTarget, ws: true },
    },
  },
  test: {
    environment: 'jsdom',
    // e2e/ — сценарии Playwright (npm run e2e), не юнит-тесты.
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})

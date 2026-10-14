/// <reference lib="webworker" />
import { clientsClaim } from 'workbox-core'
import { ExpirationPlugin } from 'workbox-expiration'
import { createHandlerBoundToURL, precacheAndRoute } from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'
import { NetworkFirst } from 'workbox-strategies'
import type { PushPayload } from './payload'

declare const self: ServiceWorkerGlobalScope

/**
 * Service worker Mise (DESIGN.md §8):
 * — оболочка приложения (HTML, JS, CSS, шрифты, иконки) доступна без сети;
 * — GET к API: сначала сеть, при обрыве — последний ответ не старше 5 минут (остатки быстро устаревают);
 * — POST/PUT/DELETE не кэшируются никогда: заказ или поставка без сети не «притворяются» принятыми;
 * — пуши об испортившемся и стоп-листе; клик открывает нужный раздел.
 */

precacheAndRoute(self.__WB_MANIFEST)

// Любой адрес приложения без сети открывает ту же оболочку; API, хаб и служебные пути — мимо.
registerRoute(new NavigationRoute(createHandlerBoundToURL('/index.html'), { denylist: [/^\/api\//, /^\/hubs\//, /^\/health/, /^\/swagger/] }))

const API_CACHE = 'mise-api'
const API_MAX_AGE_SECONDS = 5 * 60

registerRoute(
  ({ url, request }) => request.method === 'GET' && url.origin === self.location.origin && url.pathname.startsWith('/api/v1/') && !url.pathname.startsWith('/api/v1/auth/'),
  new NetworkFirst({
    cacheName: API_CACHE,
    networkTimeoutSeconds: 4,
    plugins: [new ExpirationPlugin({ maxAgeSeconds: API_MAX_AGE_SECONDS, maxEntries: 150 })],
  }),
)

// Обновление — только по согласию пользователя («Доступно обновление → Обновить»), не посреди заказа.
self.addEventListener('message', (event) => {
  if (event.data?.type === 'SKIP_WAITING') void self.skipWaiting()
  // При выходе из системы кэш ответов API чистится: следующему на этом устройстве чужие данные не достанутся.
  if (event.data?.type === 'CLEAR_API_CACHE') event.waitUntil(caches.delete(API_CACHE))
})
clientsClaim()

self.addEventListener('push', (event) => {
  const payload = readPayload(event.data)
  if (!payload) return
  event.waitUntil(
    self.registration.showNotification(payload.title, {
      body: payload.body,
      tag: payload.tag,
      icon: '/pwa-192.png',
      badge: '/badge-96.png',
      lang: payload.lang,
      data: { url: payload.url },
      // Критичное (испортилось, блюдо в стопе) остаётся на экране, пока его не увидят.
      requireInteraction: payload.severity === 'Critical',
    }),
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const target = new URL((event.notification.data as { url?: string } | null)?.url ?? '/', self.location.origin).href
  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
      const open = windows.find((client) => new URL(client.url).origin === self.location.origin)
      if (open) {
        await open.focus()
        await open.navigate(target)
      } else {
        await self.clients.openWindow(target)
      }
    })(),
  )
})

function readPayload(data: PushMessageData | null): PushPayload | null {
  if (!data) return null
  try {
    const payload = data.json() as PushPayload
    return payload.title ? payload : null
  } catch {
    return { title: 'Mise', body: data.text(), url: '/alerts', tag: 'mise', severity: 'Info', lang: 'ru' }
  }
}

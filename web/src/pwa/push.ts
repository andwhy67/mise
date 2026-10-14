import { useCallback, useEffect, useState } from 'react'
import { api } from '@/api/client'
import { session } from '@/auth/session'
import { isIos, isStandalone } from './install'

/**
 * Пуши на телефон (Web Push): подписка этого устройства на уведомления текущего пользователя.
 * Сервер шлёт только то, по чему нужно действовать (испортилось, блюдо в стопе, мало продукта), — по роли.
 */
interface PushConfig {
  enabled: boolean
  publicKey: string | null
}

export type PushState =
  | 'loading'
  /** Браузер не умеет пуши (или это dev-сборка без service worker). */
  | 'unsupported'
  /** iPhone/iPad: пуши есть только у приложения, установленного на экран «Домой». */
  | 'needsInstall'
  /** На сервере не задан ключ VAPID. */
  | 'serverOff'
  /** Пользователь запретил уведомления в настройках браузера. */
  | 'denied'
  | 'off'
  | 'on'

const supported = () => 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window

/** Ключ VAPID (base64url) → байты для pushManager.subscribe. */
function keyBytes(base64url: string): Uint8Array<ArrayBuffer> {
  const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(base64url.length / 4) * 4, '=')
  const raw = atob(base64)
  const bytes = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i)
  return bytes
}

/** Регистрация service worker, если она есть (в dev её нет — ждать ready бессмысленно). */
async function registration(): Promise<ServiceWorkerRegistration | null> {
  if (!supported()) return null
  return (await navigator.serviceWorker.getRegistration()) ?? null
}

async function currentSubscription() {
  return (await registration())?.pushManager.getSubscription() ?? null
}

async function sendSubscription(subscription: PushSubscription, language: string) {
  const json = subscription.toJSON()
  await api.post('/api/v1/push/subscribe', { endpoint: json.endpoint, keys: json.keys, language })
}

/**
 * Синхронизация при входе и смене языка: подписка устройства принадлежит вошедшему сотруднику и
 * пишет на его языке. Новой подписки без согласия не создаёт — только обновляет существующую.
 */
export async function syncPushSubscription(language: string) {
  try {
    if (!supported() || Notification.permission !== 'granted') return
    const subscription = await currentSubscription()
    if (subscription) await sendSubscription(subscription, language)
  } catch {
    // Не удалось — не страшно: пуши придут после следующего входа или переключения.
  }
}

/** При выходе устройство отписывается: на общем планшете чужие уведомления не нужны следующему. */
export function initPushLogout() {
  session.beforeLogout(async () => {
    const subscription = await currentSubscription()
    if (!subscription) return
    await api.post('/api/v1/push/unsubscribe', { endpoint: subscription.endpoint }).catch(() => undefined)
    await subscription.unsubscribe()
  })
}

export function usePush(language: string) {
  const [state, setState] = useState<PushState>('loading')
  const [config, setConfig] = useState<PushConfig | null>(null)
  const [busy, setBusy] = useState(false)

  const refresh = useCallback(async () => {
    if (!supported()) return setState(isIos() && !isStandalone() ? 'needsInstall' : 'unsupported')
    if (!(await registration())) return setState('unsupported')
    const loaded = await api.get<PushConfig>('/api/v1/push/config').catch(() => null)
    setConfig(loaded)
    if (!loaded?.enabled || !loaded.publicKey) return setState('serverOff')
    if (Notification.permission === 'denied') return setState('denied')
    setState((await currentSubscription()) ? 'on' : 'off')
  }, [])

  useEffect(() => {
    // Первичная проверка состояния: колбэк сам выставляет state после асинхронных запросов.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void refresh()
  }, [refresh])

  const enable = async () => {
    if (!config?.publicKey) return
    setBusy(true)
    try {
      // Разрешение спрашиваем только по нажатию кнопки — браузеры (и пользователи) не любят запрос «с порога».
      const permission = await Notification.requestPermission()
      if (permission !== 'granted') return setState(permission === 'denied' ? 'denied' : 'off')
      const reg = await registration()
      if (!reg) return setState('unsupported')
      const subscription =
        (await reg.pushManager.getSubscription()) ??
        (await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(config.publicKey) }))
      await sendSubscription(subscription, language)
      setState('on')
    } finally {
      setBusy(false)
    }
  }

  const disable = async () => {
    setBusy(true)
    try {
      const subscription = await currentSubscription()
      if (subscription) {
        await api.post('/api/v1/push/unsubscribe', { endpoint: subscription.endpoint })
        await subscription.unsubscribe()
      }
      setState('off')
    } finally {
      setBusy(false)
    }
  }

  const test = () => api.post<{ sent: number }>('/api/v1/push/test')

  return { state, busy, enable, disable, test }
}

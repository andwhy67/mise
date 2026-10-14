import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import type { QueryClient, QueryKey } from '@tanstack/react-query'
import { useSyncExternalStore } from 'react'
import { queryKeys } from '@/api/keys'
import { session } from '@/auth/session'

export type LiveStatus = 'connecting' | 'live' | 'reconnecting' | 'offline'

const HUB_PATH = '/hubs/stock'
const RETRY_DELAYS = [0, 2_000, 5_000, 10_000, 30_000]
const OFFLINE_RETRY_MS = 15_000

/** «Что продавать» по всему меню зависит от остатков и доступности — перечитывается по их событиям. */
const HOT_MENU: QueryKey = [...queryKeys.hotOffers, 'menu']

/** Какие данные устарели после события хаба (PLAN §7). REST — источник истины: события только инвалидируют кэш. */
const EVENT_KEYS: Record<string, QueryKey[]> = {
  StockChanged: [queryKeys.stock, queryKeys.batches, queryKeys.supplySuggestions, queryKeys.stopListForecast, HOT_MENU],
  AvailabilityChanged: [queryKeys.menu, HOT_MENU],
  StopListChanged: [queryKeys.stopList, queryKeys.menu, queryKeys.kpi],
  AlertRaised: [queryKeys.alerts],
  BatchStatusChanged: [queryKeys.batches, queryKeys.stock],
  OrderCreated: [queryKeys.orders, queryKeys.kpi],
  OrderStatusChanged: [queryKeys.orders, queryKeys.kpi],
  SupplyReceived: [queryKeys.supplies],
  SimulatorStateChanged: [queryKeys.simulator],
}

function createRealtime() {
  let status: LiveStatus = 'offline'
  let connection: HubConnection | null = null
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  const listeners = new Set<() => void>()

  const setStatus = (next: LiveStatus) => {
    if (next === status) return
    status = next
    listeners.forEach((listener) => listener())
  }

  /**
   * Запуск конкретного соединения. Попытка, начатая для старого соединения (выход и вход, двойной эффект
   * StrictMode), ничего не меняет: иначе её ошибка ставила бы «Нет связи» и повторно запускала уже живое соединение.
   */
  async function connect(hub: HubConnection, client: QueryClient) {
    if (connection !== hub) return
    clearTimeout(retryTimer)
    setStatus('connecting')
    try {
      await hub.start()
      if (connection !== hub) return
      setStatus('live')
      // После паузы данные могли измениться — перечитать всё видимое.
      await client.invalidateQueries()
    } catch {
      if (connection !== hub) return
      setStatus('offline')
      retryTimer = setTimeout(() => void connect(hub, client), OFFLINE_RETRY_MS)
    }
  }

  return {
    subscribe(listener: () => void) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    status: () => status,

    start(client: QueryClient) {
      if (connection) return
      const hub = new HubConnectionBuilder()
        .withUrl(`${(import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '')}${HUB_PATH}`, {
          accessTokenFactory: async () => (await session.freshToken()) ?? '',
        })
        .withAutomaticReconnect(RETRY_DELAYS)
        .configureLogging(LogLevel.Warning)
        .build()

      for (const [event, keys] of Object.entries(EVENT_KEYS))
        hub.on(event, () => keys.forEach((queryKey) => void client.invalidateQueries({ queryKey })))
      // Горячие предложения приходят целиком — кладём в кэш без лишнего запроса.
      // Обработчик ничего не возвращает: значение SignalR принял бы за ответ серверу («server is not expecting a result»).
      hub.on('HotOffersChanged', (payload: unknown) => {
        client.setQueryData([...queryKeys.hotOffers, 'default'], payload)
        void client.invalidateQueries({ queryKey: HOT_MENU })
      })

      hub.onreconnecting(() => setStatus('reconnecting'))
      hub.onreconnected(() => {
        setStatus('live')
        void client.invalidateQueries()
      })
      hub.onclose(() => {
        if (connection !== hub) return
        setStatus('offline')
        retryTimer = setTimeout(() => void connect(hub, client), OFFLINE_RETRY_MS)
      })

      connection = hub
      void connect(hub, client)
    },

    async stop() {
      clearTimeout(retryTimer)
      const hub = connection
      connection = null
      setStatus('offline')
      if (hub && hub.state !== HubConnectionState.Disconnected) await hub.stop()
    },
  }
}

export const realtime = createRealtime()

export function useLiveStatus(): LiveStatus {
  return useSyncExternalStore(realtime.subscribe, realtime.status, realtime.status)
}

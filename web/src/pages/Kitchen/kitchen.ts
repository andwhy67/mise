import type { Tone } from '@/lib/format'

/** Экран кухни (KDS) — чистая логика: колонки, таймеры и их пороги, новые заказы для звонка. */

export type KitchenStatus = 'Created' | 'Cooking' | 'Done'

export interface KitchenOrder {
  id: string
  number: string
  status: KitchenStatus
  createdAt: string
  /** С какого момента заказ в текущей колонке. */
  statusSince: string
  portions: number
  items: { dishName: string; quantity: number; modifications: { productName: string; kind: 'Remove' | 'Replace'; substituteName: string | null }[] }[]
  substitutions: { productName: string; substituteName: string }[]
  /** Заказ гостя из QR-меню: стол и как он заплатит. */
  source?: 'Staff' | 'Guest'
  tableNumber?: number | null
  payment?: 'Sbp' | 'Cash' | null
}

export const COLUMNS: KitchenStatus[] = ['Created', 'Cooking', 'Done']

/** Следующий шаг одной кнопкой: Принят → Готовится → Готов → Выдан. */
export const NEXT_STATUS: Record<KitchenStatus, 'Cooking' | 'Done' | 'Served'> = { Created: 'Cooking', Cooking: 'Done', Done: 'Served' }

/**
 * Сколько минут в колонке — нормально, а с какого — пора торопиться (янтарный) и уже опоздание (красный).
 * Принятый не должен ждать начала дольше 5 мин, готовка — до 15, готовый остывает на раздаче через 3 мин.
 */
export const LATE_MINUTES: Record<KitchenStatus, { warn: number; crit: number }> = {
  Created: { warn: 5, crit: 10 },
  Cooking: { warn: 15, crit: 25 },
  Done: { warn: 3, crit: 8 },
}

export function byColumn(orders: KitchenOrder[]): Record<KitchenStatus, KitchenOrder[]> {
  const columns: Record<KitchenStatus, KitchenOrder[]> = { Created: [], Cooking: [], Done: [] }
  for (const order of orders) columns[order.status]?.push(order)
  // Внутри колонки — кто дольше ждёт, тот выше.
  for (const status of COLUMNS) columns[status].sort((a, b) => a.statusSince.localeCompare(b.statusSince))
  return columns
}

export const elapsedMs = (since: string, now: Date) => Math.max(0, now.getTime() - Date.parse(since))

/** «4:07», после часа — «1:04:07». */
export function formatTimer(ms: number) {
  const total = Math.floor(ms / 1000)
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = String(total % 60).padStart(2, '0')
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`
}

export function lateness(order: Pick<KitchenOrder, 'status' | 'statusSince'>, now: Date): Tone {
  const minutes = elapsedMs(order.statusSince, now) / 60_000
  const limits = LATE_MINUTES[order.status]
  return minutes >= limits.crit ? 'crit' : minutes >= limits.warn ? 'warn' : 'fresh'
}

/**
 * Новые принятые заказы с прошлого обновления — для звонка. Первая загрузка (known = null) не звонит:
 * открыть экран — не значит получить новый заказ.
 */
export function newlyCreated(known: ReadonlySet<string> | null, orders: KitchenOrder[]): string[] {
  if (known === null) return []
  return orders.filter((o) => o.status === 'Created' && !known.has(o.id)).map((o) => o.id)
}

import type { EventItem } from '@/api/planning'

/** Порции из полей ввода: пустые и нулевые строки не идут в расчёт. */
export function toEventItems(portions: Record<string, string>): EventItem[] {
  return Object.entries(portions)
    .map(([dishId, value]) => ({ dishId, portions: Number(value) }))
    .filter((item) => Number.isInteger(item.portions) && item.portions > 0)
}

export const portionsCount = (items: { portions: number }[]) => items.reduce((sum, item) => sum + item.portions, 0)

/** Значение для datetime-local: завтра в 19:00 по времени устройства. */
export function defaultEventTime(now = new Date()): string {
  const at = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}T19:00`
}

/** Время из datetime-local в ISO, если оно в будущем; иначе null. */
export function futureIso(value: string, now = new Date()): string | null {
  const at = new Date(value)
  return Number.isNaN(at.getTime()) || at.getTime() <= now.getTime() ? null : at.toISOString()
}

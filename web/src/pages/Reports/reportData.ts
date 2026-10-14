import type { MeasureUnit } from '@/lib/format'

/** Данные отчётов (ответы /analytics/*) и их подготовка к графикам — чистые функции. */

export type ReportPeriod = 'today' | 'week' | 'month'

export interface KpiReport {
  from: string
  to: string
  period: {
    orders: number
    cancelledOrders: number
    revenue: number
    averageTicket: number
    writeOffRate: number
    worstWriteOffRates: WriteOffRate[]
    dishMinutesInStop: number
    averageDishesInStop: number
  }
}

export interface WriteOffRate {
  productId: string
  productName: string
  unit: MeasureUnit
  receivedQty: number
  writtenOffQty: number
  rate: number
}

export interface WriteOffReport {
  from: string
  to: string
  expiryOperations: number
  manualOperations: number
  byDay: { date: string; expiryOperations: number; manualOperations: number; products: number }[]
  byProduct: { productId: string; productName: string; unit: MeasureUnit; expiryQty: number; manualQty: number; totalQty: number; operations: number }[]
}

export interface DeficitItem {
  productId: string
  productName: string
  stopEvents: number
  dishesAffected: number
  dishMinutesInStop: number
  minutesBelowMinimum: number
  shareBelowMinimum: number
}

export interface ConsumptionReport {
  from: string
  to: string
  bucket: 'Hour' | 'Day'
  series: { productId: string; productName: string; unit: MeasureUnit; total: number; points: { bucket: string; qty: number }[] }[]
}

const MOSCOW_OFFSET_MS = 3 * 3_600_000
const DAY_MS = 86_400_000
const PERIOD_DAYS: Record<ReportPeriod, number> = { today: 1, week: 7, month: 30 }

/** Полночь по Москве N-1 дней назад: «7 дней» — это сегодня и шесть предыдущих. */
export function periodRange(period: ReportPeriod, now: Date): { from: string; bucket: 'Hour' | 'Day' } {
  const moscow = new Date(now.getTime() + MOSCOW_OFFSET_MS)
  const midnight = Date.UTC(moscow.getUTCFullYear(), moscow.getUTCMonth(), moscow.getUTCDate()) - MOSCOW_OFFSET_MS
  const from = new Date(midnight - (PERIOD_DAYS[period] - 1) * DAY_MS)
  return { from: from.toISOString(), bucket: period === 'today' ? 'Hour' : 'Day' }
}

/** Московские даты (YYYY-MM-DD) от начала периода до сегодня. */
export function daysInPeriod(from: string, now: Date): string[] {
  const days: string[] = []
  const end = new Date(now.getTime() + MOSCOW_OFFSET_MS).toISOString().slice(0, 10)
  for (let t = new Date(from).getTime() + MOSCOW_OFFSET_MS; ; t += DAY_MS) {
    const day = new Date(t).toISOString().slice(0, 10)
    days.push(day)
    if (day >= end) break
  }
  return days
}

export function writeOffDays(byDay: WriteOffReport['byDay'], days: string[]) {
  return days.map((date) => {
    const day = byDay.find((d) => d.date === date)
    return { date, expiry: day?.expiryOperations ?? 0, manual: day?.manualOperations ?? 0 }
  })
}

const round1 = (value: number) => Math.round(value * 10) / 10

export function worstRates(rates: WriteOffRate[], limit: number) {
  return rates
    .filter((r) => r.rate > 0)
    .sort((a, b) => b.rate - a.rate)
    .slice(0, limit)
    .map((item) => ({ name: item.productName, percent: round1(item.rate * 100), item }))
}

export function stopCauses(items: DeficitItem[], limit: number) {
  return items
    .filter((i) => i.stopEvents > 0)
    .sort((a, b) => b.dishMinutesInStop - a.dishMinutesInStop)
    .slice(0, limit)
    .map((item) => ({ name: item.productName, hours: round1(item.dishMinutesInStop / 60), item }))
}

/** Ряд расхода по суткам: сервер присылает только сутки с данными — пропуски заполняем нулями, иначе линия «перепрыгивает». */
export function consumptionByDay(points: { bucket: string; qty: number }[], days: string[]) {
  const byDay = new Map<string, number>()
  for (const point of points) {
    const day = new Date(new Date(point.bucket).getTime() + MOSCOW_OFFSET_MS).toISOString().slice(0, 10)
    byDay.set(day, (byDay.get(day) ?? 0) + point.qty)
  }
  return days.map((date) => ({ date, qty: byDay.get(date) ?? 0 }))
}

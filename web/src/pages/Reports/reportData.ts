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
  unit: MeasureUnit
  /** Отказов в заказах (422) из-за нехватки продукта. */
  refusals: number
  /** Сколько не хватило по этим отказам, базовые единицы. */
  shortageQty: number
  lastRefusalAt: string | null
  stopEvents: number
  dishesAffected: number
  dishMinutesInStop: number
  minutesBelowMinimum: number
  shareBelowMinimum: number
}

/** Деньги за период (₽): суммы — по цене партии в момент операции. */
export interface MoneyReport {
  from: string
  to: string
  revenue: number
  purchases: number
  costOfSales: number
  grossMargin: number
  losses: number
  expiryLosses: number
  manualLosses: number
  /** Продано из партий на грани срока — иначе ушло бы в списание. */
  rescued: number
  /** Доля движений с известной ценой (0…1). */
  costCoverage: number
  products: MoneyProduct[]
}

export interface MoneyProduct {
  productId: string
  productName: string
  unit: MeasureUnit
  purchased: number
  costOfSales: number
  losses: number
  expiryLosses: number
  rescued: number
  rescuedQty: number
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


/** Ряд расхода по суткам: сервер присылает только сутки с данными — пропуски заполняем нулями, иначе линия «перепрыгивает». */
export function consumptionByDay(points: { bucket: string; qty: number }[], days: string[]) {
  const byDay = new Map<string, number>()
  for (const point of points) {
    const day = new Date(new Date(point.bucket).getTime() + MOSCOW_OFFSET_MS).toISOString().slice(0, 10)
    byDay.set(day, (byDay.get(day) ?? 0) + point.qty)
  }
  return days.map((date) => ({ date, qty: byDay.get(date) ?? 0 }))
}

/**
 * «Чего чаще всего не хватает»: сначала то, из-за чего уже отказали гостю, затем — что ставило блюда в стоп.
 * Продукты без отказов и без стопов (только «ниже минимума») в график не попадают — это видно в таблице склада.
 */
export function deficitRanking(items: DeficitItem[], limit: number) {
  return items
    .filter((i) => i.refusals > 0 || i.stopEvents > 0)
    .sort((a, b) => b.refusals - a.refusals || b.stopEvents - a.stopEvents || b.dishMinutesInStop - a.dishMinutesInStop || a.productName.localeCompare(b.productName))
    .slice(0, limit)
    .map((item) => ({ name: item.productName, refusals: item.refusals, stops: item.stopEvents, item }))
}

/** Доля валовой маржи в выручке, 0…1; без выручки — null (делить не на что). */
export const marginShare = (money: Pick<MoneyReport, 'revenue' | 'grossMargin'>) => (money.revenue > 0 ? money.grossMargin / money.revenue : null)

/**
 * «Где теряем и что спасли»: продукты с потерями или спасёнными деньгами, сначала самые дорогие по потерям.
 * Рубли округлены до целых — на графике копейки только шумят.
 */
export function moneyRanking(products: MoneyProduct[], limit: number) {
  return products
    .filter((p) => p.losses > 0 || p.rescued > 0)
    .sort((a, b) => b.losses - a.losses || b.rescued - a.rescued || a.productName.localeCompare(b.productName))
    .slice(0, limit)
    .map((item) => ({ name: item.productName, losses: Math.round(item.losses), rescued: Math.round(item.rescued), item }))
}

/**
 * Прошлый период для сравнения — той же длины и до того же момента: «эта неделя до сейчас» против
 * «прошлой недели до того же часа», а «сегодня до 14:00» — против «вчера до 14:00». Иначе неполный день проигрывает полному.
 */
export function previousRange(period: ReportPeriod, from: string, now: Date): { from: string; to: string } {
  const shift = PERIOD_DAYS[period] * DAY_MS
  return { from: new Date(new Date(from).getTime() - shift).toISOString(), to: new Date(now.getTime() - shift).toISOString() }
}

/** Какое изменение хорошее: выручка — рост, потери — снижение; нейтральное — просто факт. */
export type Better = 'up' | 'down' | 'neutral'

export interface Comparison {
  direction: 'up' | 'down' | 'flat'
  /** Изменение в процентах от прошлого значения; null — прошлого не было (0), сравнивать не с чем. */
  percent: number | null
  tone: 'good' | 'bad' | 'neutral'
}

/** Меньше 1 % — «без изменений»: шум не должен краситься в красный. */
const FLAT_PERCENT = 1

const toneOf = (direction: Comparison['direction'], better: Better): Comparison['tone'] =>
  direction === 'flat' || better === 'neutral' ? 'neutral' : direction === better ? 'good' : 'bad'

export function compare(current: number, previous: number, better: Better): Comparison {
  if (previous === 0) {
    const direction = current > 0 ? 'up' : current < 0 ? 'down' : 'flat'
    return { direction, percent: null, tone: toneOf(direction, better) }
  }
  const percent = Math.round(((current - previous) / Math.abs(previous)) * 1000) / 10
  const direction = Math.abs(percent) < FLAT_PERCENT ? 'flat' : percent > 0 ? 'up' : 'down'
  return { direction, percent, tone: toneOf(direction, better) }
}

/** Доли (списание 3 % → 5 %) сравниваем в процентных пунктах, а не «+66 %». */
export function comparePoints(current: number, previous: number, better: Better): Comparison & { points: number } {
  const points = Math.round((current - previous) * 1000) / 10
  const direction = Math.abs(points) < 0.1 ? 'flat' : points > 0 ? 'up' : 'down'
  return { direction, percent: null, tone: toneOf(direction, better), points }
}

import type { Product, SupplySuggestion } from '@/api/types'
import type { MeasureUnit } from '@/lib/format'

/**
 * Заявки поставщикам из «Что заказать» — чистая логика: группировка по поставщику, правка количеств, текст заявки.
 * Количества в форме — в кг / л / шт (как пишут поставщику), в запросе к API — в базовой единице.
 */

export interface RequestLine {
  productId: string
  productName: string
  unit: MeasureUnit
  /** Количество в кг / л / шт — строкой, как в поле ввода. */
  quantity: string
  /** Справочная цена за кг / л / шт; null — неизвестна. */
  price: number | null
  include: boolean
}

export interface SupplierGroup {
  /** null — у продуктов поставщик не указан. */
  supplier: string | null
  lines: RequestLine[]
}

const factor = (unit: MeasureUnit) => (unit === 'Piece' ? 1 : 1000)
const parse = (value: string) => (value.trim() === '' ? NaN : Number(value.replace(',', '.').replace(/\s/g, '')))
const show = (value: number) => String(Math.round(value * 1000) / 1000).replace('.', ',')

/** По поставщикам (по алфавиту, «без поставщика» — в конце), внутри — по названию. */
export function groupBySupplier(items: SupplySuggestion[], products: Product[]): SupplierGroup[] {
  const groups = new Map<string | null, RequestLine[]>()
  for (const item of items) {
    const product = products.find((p) => p.id === item.productId)
    const supplier = product?.supplier?.trim() || null
    const line: RequestLine = {
      productId: item.productId,
      productName: item.productName,
      unit: item.unit,
      quantity: show(item.suggestedQty / factor(item.unit)),
      price: product?.referencePrice ?? null,
      include: true,
    }
    groups.set(supplier, [...(groups.get(supplier) ?? []), line])
  }
  return [...groups.entries()]
    .map(([supplier, lines]) => ({ supplier, lines: lines.sort((a, b) => a.productName.localeCompare(b.productName)) }))
    .sort((a, b) => (a.supplier === null ? 1 : b.supplier === null ? -1 : a.supplier.localeCompare(b.supplier)))
}

/** Количество строки в базовой единице; null — поле заполнено неверно. */
export function baseQuantity(line: RequestLine): number | null {
  const value = parse(line.quantity)
  return value > 0 ? Math.round(value * factor(line.unit) * 1000) / 1000 : null
}

export const includedLines = (group: SupplierGroup) => group.lines.filter((l) => l.include)

/** Ориентировочная сумма по справочным ценам и сколько строк без цены. */
export function groupEstimate(group: SupplierGroup) {
  let total = 0
  let unpriced = 0
  for (const line of includedLines(group)) {
    const qty = parse(line.quantity)
    if (line.price == null || !(qty > 0)) unpriced += 1
    else total += qty * line.price
  }
  return { total: Math.round(total * 100) / 100, unpriced }
}

/** Ошибки по productId: количество в отмеченной строке должно быть больше нуля. */
export function validateGroup(group: SupplierGroup): Record<string, string> {
  const errors: Record<string, string> = {}
  for (const line of includedLines(group)) if (baseQuantity(line) === null) errors[line.productId] = 'quantity'
  return errors
}

export const toRequest = (group: SupplierGroup) => ({
  supplier: group.supplier,
  lines: includedLines(group).map((l) => ({ productId: l.productId, quantity: baseQuantity(l)! })),
})

export interface RequestTextLabels {
  title: (number: string) => string
  supplier: (name: string) => string
  noSupplier: string
  date: (date: string) => string
  line: (index: number, product: string, qty: string) => string
  estimate: (sum: string) => string
  estimatePartial: (sum: string, count: number) => string
}

/** Текст заявки для мессенджера или почты: номер, кому, дата, строки, ориентировочная сумма. */
export function requestText(
  group: SupplierGroup,
  meta: { number: string; date: string },
  labels: RequestTextLabels,
  format: { qtyExact: (value: number, unit: MeasureUnit) => string; money: (value: number) => string },
): string {
  const lines = includedLines(group).map((l, i) => labels.line(i + 1, l.productName, format.qtyExact(baseQuantity(l) ?? 0, l.unit)))
  const { total, unpriced } = groupEstimate(group)
  const estimate = total > 0 ? [unpriced > 0 ? labels.estimatePartial(format.money(total), unpriced) : labels.estimate(format.money(total))] : []
  return [
    labels.title(meta.number),
    group.supplier ? labels.supplier(group.supplier) : labels.noSupplier,
    labels.date(meta.date),
    '',
    ...lines,
    ...(estimate.length ? ['', ...estimate] : []),
  ].join('\n')
}

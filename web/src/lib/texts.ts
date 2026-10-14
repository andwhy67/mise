import type { TFunction } from 'i18next'
import type { Batch, HotOffer, StopListForecastItem, StopListItem, SupplySuggestion } from '@/api/types'
import type { ExpiryItem } from '@/components/ExpiryList'
import type { Formatter } from '@/lib/format'

/** Тексты экранов из структурных данных API — на языке интерфейса (DESIGN.md §1.1, §9a). */

export function offerReason(offer: HotOffer, f: Formatter, t: TFunction<'dashboard'>): string {
  const [first, second] = offer.reasons
  if (!first) return ''
  const parts = [
    t('offers.first', { product: first.productName, qty: f.qty(first.atRiskQty, first.unit), when: f.whenInline(first.expiresAt) }),
  ]
  if (second) parts.push(t('offers.next', { product: second.productName, qty: f.qty(second.atRiskQty, second.unit), when: f.whenInline(second.expiresAt) }))
  const portions = Math.max(...offer.reasons.map((r) => r.portionsBurned))
  const sentence = `${parts.join(', ')}.`
  return portions > 0 ? `${sentence} ${t('offers.portions', { portions: f.portionsUpTo(portions) })}` : sentence
}

/** «Как это посчитано»: технические числа допустимы только здесь (DESIGN.md §1.1, п. 6). */
export function offerCalculation(offer: HotOffer, f: Formatter, t: TFunction<'dashboard'>): string[] {
  const percent = (value: number) => new Intl.NumberFormat(f.locale, { style: 'percent', maximumFractionDigits: 0 }).format(value)
  return [
    t('offers.calculation.score', { score: f.number(offer.score) }),
    ...offer.reasons.map((r) => {
      const base = t('offers.calculation.reason', { product: r.productName, atRisk: f.qty(r.atRiskQty, r.unit), when: f.whenInline(r.expiresAt) })
      const unsold = r.unsoldQty !== null && r.unsoldQty < r.atRiskQty ? `; ${t('offers.calculation.unsold', { qty: f.qty(r.unsoldQty, r.unit) })}` : ''
      return `${base}${unsold}; ${t('offers.calculation.share', { share: percent(r.share) })}.`
    }),
    t('offers.calculation.formula'),
  ]
}

export function stopReason(item: StopListItem, f: Formatter, t: TFunction<'dashboard'>): string {
  const product = item.limitingProduct
  if (item.reason === 'Manual' || !product) return t('stop.manual')
  return t('stop.shortage', { product: product.productName, left: f.qty(product.usable, product.unit), need: f.qty(product.qtyPerPortion, product.unit) })
}

export function forecastReason(item: StopListForecastItem, f: Formatter, t: TFunction<'dashboard'>): string {
  const product = item.limitingProduct.productName
  return item.cause === 'Expiry'
    ? t('stop.soonExpiry', { product, when: f.whenInline(item.eta) })
    : t('stop.soonConsumption', { product, runway: f.runway(item.hoursLeft).toLocaleLowerCase(f.locale) })
}

/** «Что скоро испортится»: по продукту — сколько истекает в окне и когда раньше всего. */
export function expiryItems(batches: Batch[], f: Formatter): ExpiryItem[] {
  const byProduct = new Map<string, Batch[]>()
  for (const batch of batches.filter((b) => b.remainingQty > 0)) byProduct.set(batch.productId, [...(byProduct.get(batch.productId) ?? []), batch])
  return [...byProduct.values()]
    .map((group) => {
      const earliest = group.reduce((a, b) => (a.expiresAt <= b.expiresAt ? a : b))
      const qty = group.reduce((sum, b) => sum + b.remainingQty, 0)
      return {
        id: earliest.productId,
        productName: earliest.productName,
        quantity: f.qty(qty, earliest.unit),
        when: f.when(earliest.expiresAt, earliest.status === 'Expired' ? 'Expired' : earliest.shelfLife.status),
        sortKey: earliest.expiresAt,
      }
    })
    .sort((a, b) => a.sortKey.localeCompare(b.sortKey))
    .map((item) => ({ id: item.id, productName: item.productName, quantity: item.quantity, when: item.when }))
}

export function supplyLine(item: SupplySuggestion, f: Formatter, t: TFunction<'dashboard'>): string {
  const parts: string[] = []
  if (item.runsOutAt) parts.push(t('toOrder.runsOut', { when: f.whenInline(item.runsOutAt) }))
  if (item.expectedWaste > 0) parts.push(t('toOrder.waste', { qty: f.qty(item.expectedWaste, item.unit) }))
  if (item.expectedDemand <= 0 && item.confidence === 'Low') parts.push(t('toOrder.noHistory'))
  else if (!item.runsOutAt) parts.push(t('toOrder.belowMin'))
  const text = parts.join('; ')
  return text.charAt(0).toLocaleUpperCase(f.locale) + text.slice(1) + '.'
}

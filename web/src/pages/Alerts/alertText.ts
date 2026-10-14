import type { TFunction } from 'i18next'
import type { Alert } from '@/api/types'
import type { Formatter, Tone } from '@/lib/format'

/**
 * Текст уведомления на языке интерфейса из структурных полей (DESIGN.md §9a).
 * Если нужных полей нет (старые записи) — запасной вариант: текст сервера.
 */
export function alertText(alert: Alert, f: Formatter, t: TFunction<'alerts'>): string {
  const { productName: product, dishName: dish, unit, quantity, limitQty } = alert
  const qty = (value: number | null) => (value !== null && unit ? f.qty(value, unit) : null)

  switch (alert.type) {
    case 'ExpiringSoon':
      if (!product || !alert.expiresAt) break
      return qty(quantity)
        ? t('text.expiringQty', { product, qty: qty(quantity), when: f.whenInline(alert.expiresAt) })
        : t('text.expiring', { product, when: f.whenInline(alert.expiresAt) })
    case 'Expired':
      if (!product) break
      if (!qty(quantity)) return t('text.expired', { product })
      return alert.autoWrittenOff ? t('text.expiredAuto', { product, qty: qty(quantity) }) : t('text.expiredManual', { product, qty: qty(quantity) })
    case 'LowStock':
      if (!product || !qty(quantity)) break
      return qty(limitQty) ? t('text.low', { product, qty: qty(quantity), limit: qty(limitQty) }) : t('text.lowNoLimit', { product, qty: qty(quantity) })
    case 'StopListOn':
      if (!dish) break
      return product ? t('text.stopOn', { dish, product }) : t('text.stopManual', { dish })
    case 'StopListOff':
      if (!dish) break
      return t('text.stopOff', { dish })
  }
  return alert.message
}

/** Цвет маркера: испортившееся — «просрочено», возврат в меню — хорошая новость, остальное — по важности. */
export function alertTone(alert: Alert): Tone {
  if (alert.type === 'Expired') return 'expired'
  if (alert.type === 'StopListOff') return 'fresh'
  return alert.severity === 'Critical' ? 'crit' : alert.severity === 'Warning' ? 'warn' : 'info'
}

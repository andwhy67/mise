import { beforeAll, describe, expect, it } from 'vitest'
import type { Alert } from '@/api/types'
import { createFormatter, type Formatter } from '@/lib/format'
import { createTestI18n } from '@/test/i18n'
import type { TFunction } from 'i18next'
import { alertText, alertTone } from './alertText'

const NOW = new Date('2026-10-14T11:07:00Z')

let f: Formatter
let t: TFunction<'alerts'>
let fe: Formatter
let te: TFunction<'alerts'>

beforeAll(async () => {
  const ru = await createTestI18n('ru')
  const en = await createTestI18n('en')
  f = createFormatter(ru, () => NOW)
  t = ru.getFixedT('ru', 'alerts')
  fe = createFormatter(en, () => NOW)
  te = en.getFixedT('en', 'alerts')
})

const base: Alert = {
  id: '1',
  type: 'ExpiringSoon',
  severity: 'Warning',
  message: 'серверный текст',
  batchId: null,
  productId: 'p',
  productName: 'Шпинат',
  dishId: null,
  dishName: null,
  createdAt: NOW.toISOString(),
  acknowledgedAt: null,
  resolvedAt: null,
  isActive: true,
  unit: 'Gram',
  quantity: null,
  limitQty: null,
  expiresAt: '2026-10-14T20:00:00Z',
  autoWrittenOff: null,
}

describe('тексты уведомлений из структурных полей', () => {
  it.each([
    ['скоро испортится', { ...base }, 'Шпинат испортится сегодня в 23:00'],
    ['скоро испортится, с количеством', { ...base, quantity: 600 }, 'Шпинат: 600 г испортится сегодня в 23:00'],
    ['испортилось, списано автоматически', { ...base, type: 'Expired', quantity: 2400, autoWrittenOff: true }, 'Шпинат: 2,4 кг испортилось и списано автоматически'],
    ['испортилось, нужно списать', { ...base, type: 'Expired', quantity: 300, autoWrittenOff: false }, 'Шпинат: 300 г испортилось — нужно списать'],
    ['мало', { ...base, type: 'LowStock', quantity: 900, limitQty: 2000 }, 'Шпинат: осталось 900 г — меньше нормы (2 кг)'],
    ['в стоп-листе', { ...base, type: 'StopListOn', dishName: 'Капрезе', productName: 'Моцарелла' }, '«Капрезе» нельзя заказать: закончилось — Моцарелла'],
    ['в стоп-листе вручную', { ...base, type: 'StopListOn', dishName: 'Блины', productName: null }, '«Блины» убрали из меню'],
    ['снова можно', { ...base, type: 'StopListOff', dishName: 'Капрезе' }, '«Капрезе» снова можно заказать'],
  ] as [string, Alert, string][])('%s', (_, alert, text) => {
    expect(alertText(alert, f, t)).toBe(text)
  })

  it('нет нужных полей — текст сервера', () => {
    expect(alertText({ ...base, type: 'LowStock', productName: null }, f, t)).toBe('серверный текст')
  })

  it('en', () => {
    expect(alertText({ ...base, type: 'StopListOff', dishName: 'Caprese' }, fe, te)).toBe('“Caprese” can be ordered again')
  })

  it('тон: испортилось — expired, иначе по важности', () => {
    expect(alertTone({ ...base, type: 'Expired', severity: 'Critical' })).toBe('expired')
    expect(alertTone({ ...base, severity: 'Critical' })).toBe('crit')
    expect(alertTone({ ...base, type: 'StopListOff', severity: 'Info' })).toBe('fresh')
    expect(alertTone({ ...base, severity: 'Info' })).toBe('info')
  })
})

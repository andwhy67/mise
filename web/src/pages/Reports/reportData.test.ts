import { describe, expect, it } from 'vitest'
import { consumptionByDay, daysInPeriod, periodRange, stopCauses, worstRates, writeOffDays } from './reportData'

// Среда, 14 октября 2026, 23:30 по Москве.
const NOW = new Date('2026-10-14T20:30:00Z')

describe('период отчёта', () => {
  it('«сегодня» — с полуночи по Москве', () => {
    expect(periodRange('today', NOW)).toEqual({ from: '2026-10-13T21:00:00.000Z', bucket: 'Hour' })
  })

  it('7 и 30 дней — включая сегодня, с полуночи по Москве', () => {
    expect(periodRange('week', NOW)).toEqual({ from: '2026-10-07T21:00:00.000Z', bucket: 'Day' })
    expect(periodRange('month', NOW)).toEqual({ from: '2026-09-14T21:00:00.000Z', bucket: 'Day' })
  })

  it('дни периода — московские даты', () => {
    expect(daysInPeriod('2026-10-11T21:00:00.000Z', NOW)).toEqual(['2026-10-12', '2026-10-13', '2026-10-14'])
  })
})

describe('списания по дням', () => {
  it('пустые дни — нулями, чтобы на графике не было дыр', () => {
    const days = ['2026-10-12', '2026-10-13', '2026-10-14']
    expect(writeOffDays([{ date: '2026-10-14', expiryOperations: 9, manualOperations: 2, products: 6 }], days)).toEqual([
      { date: '2026-10-12', expiry: 0, manual: 0 },
      { date: '2026-10-13', expiry: 0, manual: 0 },
      { date: '2026-10-14', expiry: 9, manual: 2 },
    ])
  })
})

describe('что чаще пропадает', () => {
  it('доля списанного от полученного, в процентах, только ненулевые, по убыванию', () => {
    const rates = [
      { productId: 'a', productName: 'Лосось', unit: 'Gram' as const, receivedQty: 45400, writtenOffQty: 8000, rate: 0.1762 },
      { productId: 'b', productName: 'Соль', unit: 'Gram' as const, receivedQty: 1000, writtenOffQty: 0, rate: 0 },
      { productId: 'c', productName: 'Филе', unit: 'Gram' as const, receivedQty: 64000, writtenOffQty: 5340, rate: 0.0834 },
    ]
    expect(worstRates(rates, 5)).toEqual([
      { name: 'Лосось', percent: 17.6, item: rates[0] },
      { name: 'Филе', percent: 8.3, item: rates[2] },
    ])
  })
})

describe('из-за чего блюда попадают в стоп', () => {
  it('часы блюд в стопе по продукту, только с попаданиями, по убыванию', () => {
    const items = [
      { productId: 'a', productName: 'Молоко', stopEvents: 3, dishesAffected: 3, dishMinutesInStop: 8351, minutesBelowMinimum: 34, shareBelowMinimum: 0.003 },
      { productId: 'b', productName: 'Соль', stopEvents: 0, dishesAffected: 0, dishMinutesInStop: 0, minutesBelowMinimum: 0, shareBelowMinimum: 0 },
      { productId: 'c', productName: 'Бекон', stopEvents: 1, dishesAffected: 1, dishMinutesInStop: 31, minutesBelowMinimum: 0, shareBelowMinimum: 0 },
    ]
    expect(stopCauses(items, 6)).toEqual([
      { name: 'Молоко', hours: 139.2, item: items[0] },
      { name: 'Бекон', hours: 0.5, item: items[2] },
    ])
  })
})

describe('расход по дням', () => {
  it('точки — в московские сутки, дни без расхода — нулями', () => {
    const points = [
      { bucket: '2026-10-11T21:00:00+00:00', qty: 0 },
      { bucket: '2026-10-13T21:00:00+00:00', qty: 13320 },
    ]
    expect(consumptionByDay(points, ['2026-10-12', '2026-10-13', '2026-10-14'])).toEqual([
      { date: '2026-10-12', qty: 0 },
      { date: '2026-10-13', qty: 0 },
      { date: '2026-10-14', qty: 13320 },
    ])
  })
})

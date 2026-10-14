import { describe, expect, it } from 'vitest'
import type { Product } from '@/api/types'
import { emptyRow, fromMoscowInput, previewExpiry, rowFromSuggestion, toMoscowInput, toRequest, validateSupply, type SupplyFormState } from './supplyForm'

const spinach: Product = {
  id: 'spinach',
  name: 'Шпинат',
  unit: 'Gram',
  defaultShelfLifeHours: 72,
  minStockQty: 500,
  packSize: 1000,
  category: 'Овощи',
  storageType: null,
}
const salt: Product = { ...spinach, id: 'salt', name: 'Соль', defaultShelfLifeHours: null }
const products = [spinach, salt]

// Поставка 14 октября 2026, 10:00 по Москве.
const created = '2026-10-14T10:00'

const form = (rows: SupplyFormState['rows']): SupplyFormState => ({ number: 'SUP-1', supplier: 'Овощная база', createdAt: created, rows })

describe('время по Москве в полях формы', () => {
  it('ввод трактуется как московское время', () => {
    expect(fromMoscowInput('2026-10-14T10:00').toISOString()).toBe('2026-10-14T07:00:00.000Z')
  })

  it('и обратно', () => {
    expect(toMoscowInput(new Date('2026-10-14T07:00:00Z'))).toBe('2026-10-14T10:00')
  })
})

describe('предпросмотр срока — тот же приоритет, что на сервере (CLAUDE.md, правило 1)', () => {
  it('явная дата «годен до» — до конца этого дня по Москве', () => {
    const row = { ...emptyRow('r'), productId: 'spinach', quantity: '2', expiry: 'date' as const, date: '2026-10-17' }
    expect(previewExpiry(row, spinach, created)).toEqual({ expiresAt: new Date('2026-10-17T20:59:00Z'), source: 'explicit' })
  })

  it('«хранить N дней» — от даты поставки', () => {
    const row = { ...emptyRow('r'), productId: 'spinach', quantity: '2', expiry: 'days' as const, amount: '2' }
    expect(previewExpiry(row, spinach, created)).toEqual({ expiresAt: new Date('2026-10-16T07:00:00Z'), source: 'shelfLife' })
  })

  it('по умолчанию — норма продукта', () => {
    expect(previewExpiry({ ...emptyRow('r'), productId: 'spinach' }, spinach, created)).toEqual({
      expiresAt: new Date('2026-10-17T07:00:00Z'),
      source: 'default',
    })
  })

  it('«хранить N часов» — для продуктов короче суток', () => {
    const row = { ...emptyRow('r'), productId: 'spinach', quantity: '2', expiry: 'hours' as const, amount: '6' }
    expect(previewExpiry(row, spinach, created)).toEqual({ expiresAt: new Date('2026-10-14T13:00:00Z'), source: 'shelfLife' })
  })

  it('«годен до» с временем — ровно этот момент по Москве', () => {
    const row = { ...emptyRow('r'), productId: 'spinach', quantity: '2', expiry: 'date' as const, date: '2026-10-14T18:30' }
    expect(previewExpiry(row, spinach, created)).toEqual({ expiresAt: new Date('2026-10-14T15:30:00Z'), source: 'explicit' })
  })

  it('нормы нет и срок не указан — сказать нечего', () => {
    expect(previewExpiry({ ...emptyRow('r'), productId: 'salt' }, salt, created)).toEqual({ expiresAt: null, source: null })
  })
})

describe('проверка формы', () => {
  it('корректная поставка — без ошибок', () => {
    expect(validateSupply(form([{ ...emptyRow('a'), productId: 'spinach', quantity: '2,5', unit: 'Kilogram' }]), products)).toEqual({})
  })

  it('ошибки привязаны к строке и полю', () => {
    const errors = validateSupply(
      form([
        { ...emptyRow('a'), productId: '', quantity: '1' },
        { ...emptyRow('b'), productId: 'spinach', quantity: '0', unit: 'Gram' },
        { ...emptyRow('c'), productId: 'salt', quantity: '1', unit: 'Gram' },
        { ...emptyRow('d'), productId: 'spinach', quantity: '1', unit: 'Gram', expiry: 'date', date: '2026-10-13' },
        { ...emptyRow('e'), productId: 'spinach', quantity: '1', unit: 'Gram', expiry: 'days', amount: '0' },
        { ...emptyRow('f'), productId: 'spinach', quantity: '1', unit: 'Liter' },
        { ...emptyRow('g'), productId: 'spinach', quantity: '1', unit: 'Gram', expiry: 'hours', amount: '0,5' },
        { ...emptyRow('h'), productId: 'spinach', quantity: '1', unit: 'Gram', expiry: 'date', date: '2026-10-14T09:30' },
      ]),
      products,
    )
    expect(errors).toEqual({
      'a.product': 'product',
      'b.quantity': 'quantity',
      'c.expiry': 'noShelfLife',
      'd.date': 'dateBeforeDelivery',
      'e.amount': 'days',
      'f.unit': 'unit',
      'g.amount': 'hours',
      'h.date': 'dateBeforeDelivery',
    })
  })

  it('номер — буквы, цифры и . _ / -', () => {
    expect(validateSupply({ ...form([{ ...emptyRow('a'), productId: 'spinach', quantity: '1', unit: 'Gram' }]), number: 'SUP 1' }, products)).toEqual({ number: 'number' })
  })

  it('пустая поставка', () => {
    expect(validateSupply(form([]), products)).toEqual({ rows: 'empty' })
  })
})

describe('запрос к API', () => {
  it('единицы ввода, срок и дата — как ждёт сервер', () => {
    const request = toRequest(
      form([
        { ...emptyRow('a'), productId: 'spinach', quantity: '2,5', unit: 'Kilogram' },
        { ...emptyRow('b'), productId: 'spinach', quantity: '300', unit: 'Gram', expiry: 'days', amount: '1,5' },
        { ...emptyRow('c'), productId: 'spinach', quantity: '1', unit: 'Kilogram', expiry: 'date', date: '2026-10-17' },
        { ...emptyRow('d'), productId: 'spinach', quantity: '1', unit: 'Kilogram', expiry: 'hours', amount: '8' },
        { ...emptyRow('e'), productId: 'spinach', quantity: '1', unit: 'Kilogram', expiry: 'date', date: '2026-10-14T21:15' },
      ]),
    )
    expect(request).toEqual({
      number: 'SUP-1',
      supplier: 'Овощная база',
      createdAt: '2026-10-14T07:00:00.000Z',
      items: [
        { productId: 'spinach', quantity: 2.5, unit: 'Kilogram' },
        { productId: 'spinach', quantity: 300, unit: 'Gram', shelfLifeHours: 36 },
        { productId: 'spinach', quantity: 1, unit: 'Kilogram', expiresAt: '2026-10-17T20:59:00.000Z' },
        { productId: 'spinach', quantity: 1, unit: 'Kilogram', shelfLifeHours: 8 },
        { productId: 'spinach', quantity: 1, unit: 'Kilogram', expiresAt: '2026-10-14T18:15:00.000Z' },
      ],
    })
  })
})

describe('подсказка «что заказать» → строка поставки: единицы не путаются', () => {
  const lemons: Product = { ...spinach, id: 'lemons', name: 'Лимоны', unit: 'Piece' }

  it('штучный продукт остаётся в штуках', () => {
    expect(rowFromSuggestion('r', lemons, 80)).toMatchObject({ productId: 'lemons', unit: 'Piece', quantity: '80' })
  })

  it('граммы — в килограммах', () => {
    expect(rowFromSuggestion('r', spinach, 30000)).toMatchObject({ unit: 'Kilogram', quantity: '30' })
    expect(rowFromSuggestion('r', spinach, 1500)).toMatchObject({ unit: 'Kilogram', quantity: '1,5' })
  })

  it('продукта нет в справочнике — строки нет (единицу не угадываем)', () => {
    expect(rowFromSuggestion('r', undefined, 80)).toBeNull()
  })
})

import { describe, expect, it } from 'vitest'
import type { Product, SupplySuggestion } from '@/api/types'
import { baseQuantity, groupBySupplier, groupEstimate, requestText, toRequest, validateGroup, type RequestTextLabels } from './supplierRequest'

const product = (id: string, name: string, unit: Product['unit'], supplier: string | null, referencePrice: number | null): Product => ({
  id,
  name,
  unit,
  defaultShelfLifeHours: 72,
  minStockQty: 0,
  packSize: 1,
  category: null,
  storageType: null,
  referencePrice,
  supplier,
  substitutes: [],
})

const suggestion = (productId: string, productName: string, unit: Product['unit'], suggestedQty: number): SupplySuggestion => ({
  productId,
  productName,
  unit,
  usableQty: 0,
  suggestedQty,
  expectedWaste: 0,
  urgency: 'High',
  runsOutAt: null,
  confidence: 'High',
  expectedDemand: 0,
})

const products = [
  product('cream', 'Сливки', 'Milliliter', 'Молочный двор', 550),
  product('milk', 'Молоко', 'Milliliter', 'Молочный двор', 95),
  product('salmon', 'Лосось', 'Gram', 'Рыбный двор', 1900),
  product('salt', 'Соль', 'Gram', null, null),
  product('eggs', 'Яйца', 'Piece', ' ', 14),
]
const items = [
  suggestion('salmon', 'Лосось', 'Gram', 3000),
  suggestion('salt', 'Соль', 'Gram', 1000),
  suggestion('milk', 'Молоко', 'Milliliter', 12000),
  suggestion('cream', 'Сливки', 'Milliliter', 6000),
  suggestion('eggs', 'Яйца', 'Piece', 60),
]

describe('заявки поставщикам', () => {
  it('по поставщикам по алфавиту, «без поставщика» в конце; количества — в кг / л / шт', () => {
    const groups = groupBySupplier(items, products)
    expect(groups.map((g) => g.supplier)).toEqual(['Молочный двор', 'Рыбный двор', null])
    expect(groups[0]!.lines.map((l) => [l.productName, l.quantity])).toEqual([
      ['Молоко', '12'],
      ['Сливки', '6'],
    ])
    expect(groups[2]!.lines.map((l) => [l.productName, l.quantity])).toEqual([
      ['Соль', '1'],
      ['Яйца', '60'],
    ])
  })

  it('поправленное количество уходит в базовой единице; снятые строки — не уходят', () => {
    const [dairy] = groupBySupplier(items, products)
    dairy!.lines[0]!.quantity = '10,5'
    dairy!.lines[1]!.include = false
    expect(baseQuantity(dairy!.lines[0]!)).toBe(10500)
    expect(toRequest(dairy!)).toEqual({ supplier: 'Молочный двор', lines: [{ productId: 'milk', quantity: 10500 }] })
  })

  it('ориентировочная сумма по справочным ценам, строки без цены считаются отдельно', () => {
    const groups = groupBySupplier(items, products)
    expect(groupEstimate(groups[0]!)).toEqual({ total: 12 * 95 + 6 * 550, unpriced: 0 })
    expect(groupEstimate(groups[2]!)).toEqual({ total: 60 * 14, unpriced: 1 })
  })

  it('пустое или нулевое количество в отмеченной строке — ошибка', () => {
    const [dairy] = groupBySupplier(items, products)
    dairy!.lines[0]!.quantity = '0'
    dairy!.lines[1]!.quantity = ''
    dairy!.lines[1]!.include = false
    expect(validateGroup(dairy!)).toEqual({ milk: 'quantity' })
  })

  it('текст заявки — номер, кому, дата, строки и сумма', () => {
    const labels: RequestTextLabels = {
      title: (n) => `Заявка ${n}`,
      supplier: (s) => `Поставщик: ${s}`,
      noSupplier: 'Поставщик не указан',
      date: (d) => `Дата: ${d}`,
      line: (i, p, q) => `${i}. ${p} — ${q}`,
      estimate: (s) => `Ориентировочно: ${s}`,
      estimatePartial: (s, c) => `Ориентировочно: ${s} (без ${c})`,
    }
    const format = { qtyExact: (v: number, u: string) => `${v} ${u}`, money: (v: number) => `${v} ₽` }
    const [, fish] = groupBySupplier(items, products)
    expect(requestText(fish!, { number: 'REQ-1', date: '15 октября' }, labels, format)).toBe(
      ['Заявка REQ-1', 'Поставщик: Рыбный двор', 'Дата: 15 октября', '', '1. Лосось — 3000 Gram', '', 'Ориентировочно: 5700 ₽'].join('\n'),
    )
  })
})

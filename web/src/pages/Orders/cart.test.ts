import { describe, expect, it } from 'vitest'
import type { DishAvailability } from '@/api/types'
import type { MeasureUnit } from '@/lib/format'
import { activeSubstitutions, cartShortages, cartTotals, cartUsage, lineKey, orderItems, parseLine, type CartSubstitution } from './cart'

interface Product {
  productId: string
  productName: string
  unit: MeasureUnit
  usable: number
}

const milk: Product = { productId: 'milk', productName: 'Молоко', unit: 'Milliliter', usable: 1000 }
const eggs: Product = { productId: 'eggs', productName: 'Яйца', unit: 'Piece', usable: 10 }

const dish = (dishId: string, price: number, ingredients: [Product, number][]): DishAvailability => ({
  dishId,
  dishName: dishId,
  category: null,
  price,
  isActive: true,
  maxPortions: 99,
  status: 'Available',
  inStopList: false,
  limitingProduct: null,
  ingredients: ingredients.map(([p, qtyPerPortion]) => ({ ...p, qtyPerPortion, portions: Math.floor(p.usable / qtyPerPortion), removable: false, substitutes: [] })),
  alternative: null,
})

const menu = [dish('Блины', 320, [[milk, 200], [eggs, 2]]), dish('Омлет', 280, [[milk, 100], [eggs, 3]])]

describe('проверка корзины — суммарно по всем блюдам (правило 4)', () => {
  it('каждое блюдо по отдельности помещается, а вместе — нет', () => {
    // Блины ×3: 600 мл, 6 яиц; омлет ×2: 200 мл, 6 яиц → яиц нужно 12, есть 10.
    const cart = new Map([
      ['Блины', 3],
      ['Омлет', 2],
    ])
    expect(cartShortages(cart, menu)).toEqual([
      { productId: 'eggs', productName: 'Яйца', unit: 'Piece', required: 12, available: 10, shortage: 2, dishes: ['Блины', 'Омлет'], substitutes: [] },
    ])
  })

  it('всего хватает — нехватки нет', () => {
    expect(cartShortages(new Map([['Блины', 2]]), menu)).toEqual([])
  })

  it('блюда, которых нет в меню, и нулевые строки игнорируются', () => {
    expect(cartShortages(new Map([['Нет такого', 5], ['Омлет', 0]]), menu)).toEqual([])
  })

  it('итоги: порции и сумма', () => {
    expect(cartTotals(new Map([['Блины', 3], ['Омлет', 2]]), menu)).toEqual({ portions: 5, total: 1520, lines: 2 })
  })
})

describe('сколько заказ берёт от остатка — по каждому продукту', () => {
  it('сумма по всем блюдам, доля от доступного, по убыванию доли', () => {
    const cart = new Map([
      ['Блины', 2],
      ['Омлет', 1],
    ])
    expect(cartUsage(cart, menu)).toEqual([
      { productId: 'eggs', productName: 'Яйца', unit: 'Piece', required: 7, available: 10, share: 0.7 },
      { productId: 'milk', productName: 'Молоко', unit: 'Milliliter', required: 500, available: 1000, share: 0.5 },
    ])
  })

  it('пустая корзина — пусто', () => {
    expect(cartUsage(new Map(), menu)).toEqual([])
  })
})

describe('замены, на которые согласился повар', () => {
  const cream: Product = { productId: 'cream', productName: 'Сливки', unit: 'Milliliter', usable: 50 }
  const pasta = dish('Паста', 600, [[cream, 100], [milk, 20]])
  const withCream = [...menu, pasta]
  const creamToMilk: CartSubstitution = {
    productId: 'cream',
    productName: 'Сливки',
    substituteId: 'milk',
    substituteName: 'Молоко',
    unit: 'Milliliter',
    ratio: 1.5,
  }

  it('потребность переходит на заменитель и суммируется с его собственным расходом', () => {
    const cart = new Map([['Паста', 2]])
    expect(cartShortages(cart, withCream)).toMatchObject([{ productId: 'cream', required: 200, available: 50 }])
    expect(cartShortages(cart, withCream, [creamToMilk])).toEqual([])
    expect(cartUsage(cart, withCream, [creamToMilk])).toEqual([
      { productId: 'milk', productName: 'Молоко', unit: 'Milliliter', required: 340, available: 1000, share: 0.34 },
    ])
  })

  it('заменитель тоже кончается — нехватка уже по нему', () => {
    const cart = new Map([['Паста', 4]]) // 400 × 1,5 + 80 = 680 ≤ 1000 — хватает; ×8 — нет
    expect(cartShortages(new Map([['Паста', 8]]), withCream, [creamToMilk])).toMatchObject([{ productId: 'milk', required: 1360, shortage: 360 }])
    expect(cartShortages(cart, withCream, [creamToMilk])).toEqual([])
  })

  it('в запрос — только замены продуктов, которые есть в корзине', () => {
    expect(activeSubstitutions(new Map([['Блины', 1]]), withCream, [creamToMilk])).toEqual([])
    expect(activeSubstitutions(new Map([['Паста', 1]]), withCream, [creamToMilk])).toEqual([creamToMilk])
  })

  it('остаток заменителя не из меню неизвестен — не гадаем, решит сервер', () => {
    const toSoy = { ...creamToMilk, substituteId: 'soy', substituteName: 'Соевые сливки' }
    expect(cartShortages(new Map([['Паста', 2]]), withCream, [toSoy])).toEqual([])
    expect(cartUsage(new Map([['Паста', 2]]), withCream, [toSoy]).map((u) => u.productId)).toEqual(['milk'])
  })
})

describe('строки с изменениями: «без лимона», «шпинат → руккола»', () => {
  const lemon: Product = { productId: 'lemon', productName: 'Лимон', unit: 'Piece', usable: 0 }
  const spinach: Product = { productId: 'spinach', productName: 'Шпинат', unit: 'Gram', usable: 0 }
  const fish = dish('Лосось', 1190, [[lemon, 1], [spinach, 80]])
  fish.ingredients[0]!.removable = true
  fish.ingredients[1]!.substitutes = [{ productId: 'arugula', productName: 'Руккола', unit: 'Gram', ratio: 1, usable: 300 }]
  const withFish = [...menu, fish]

  it('ключ строки кодирует изменения и разбирается обратно; порядок изменений не важен', () => {
    const key = lineKey('Лосось', [{ productId: 'spinach', kind: 'Replace', substituteId: 'arugula' }, { productId: 'lemon', kind: 'Remove' }])
    expect(key).toBe(lineKey('Лосось', [{ productId: 'lemon', kind: 'Remove' }, { productId: 'spinach', kind: 'Replace', substituteId: 'arugula' }]))
    expect(parseLine(key)).toEqual({
      dishId: 'Лосось',
      changes: [{ productId: 'lemon', kind: 'Remove' }, { productId: 'spinach', kind: 'Replace', substituteId: 'arugula' }],
    })
    expect(parseLine('Блины')).toEqual({ dishId: 'Блины', changes: [] })
  })

  it('убранное не нужно, заменённое — из заменителя: лимонов и шпината нет, а заказ проходит', () => {
    const key = lineKey('Лосось', [{ productId: 'lemon', kind: 'Remove' }, { productId: 'spinach', kind: 'Replace', substituteId: 'arugula' }])
    const cart = new Map([[key, 2]])
    expect(cartShortages(cart, withFish)).toEqual([])
    expect(cartUsage(cart, withFish).map((u) => [u.productId, u.required])).toEqual([['arugula', 160]])
    expect(cartTotals(cart, withFish)).toEqual({ portions: 2, total: 2380, lines: 1 })
  })

  it('в запрос — блюдо и изменения', () => {
    const key = lineKey('Лосось', [{ productId: 'lemon', kind: 'Remove' }])
    expect(orderItems(new Map([[key, 1], ['Блины', 2]]))).toEqual([
      { dishId: 'Лосось', quantity: 1, modifications: [{ productId: 'lemon', kind: 'Remove' }] },
      { dishId: 'Блины', quantity: 2 },
    ])
  })
})

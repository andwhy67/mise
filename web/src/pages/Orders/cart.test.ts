import { describe, expect, it } from 'vitest'
import type { DishAvailability } from '@/api/types'
import type { MeasureUnit } from '@/lib/format'
import { cartShortages, cartTotals, cartUsage } from './cart'

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
  ingredients: ingredients.map(([p, qtyPerPortion]) => ({ ...p, qtyPerPortion, portions: Math.floor(p.usable / qtyPerPortion) })),
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
      { productId: 'eggs', productName: 'Яйца', unit: 'Piece', required: 12, available: 10, shortage: 2, dishes: ['Блины', 'Омлет'] },
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

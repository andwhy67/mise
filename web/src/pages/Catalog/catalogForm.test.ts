import { describe, expect, it } from 'vitest'
import type { Product } from '@/api/types'
import { dishFormToRequest, emptyDishForm, productFormToRequest, productToForm, validateDish, validateProduct } from './catalogForm'

const milk: Product = {
  id: 'milk',
  name: 'Молоко 3,2%',
  unit: 'Milliliter',
  defaultShelfLifeHours: 120,
  minStockQty: 2000,
  packSize: 1000,
  category: 'Молочные',
  storageType: 'Холодильник',
  referencePrice: 95.5,
  supplier: 'Молочный двор',
  substitutes: [{ productId: 'cream', productName: 'Сливки 10%', unit: 'Milliliter', ratio: 0.5 }],
}

describe('продукт: форма ⇄ API', () => {
  it('в форме — дни и литры, как удобно на кухне', () => {
    expect(productToForm(milk, () => 'k')).toEqual({
      name: 'Молоко 3,2%',
      unit: 'Milliliter',
      shelfLife: '5',
      shelfLifeUnit: 'days',
      minStock: '2',
      packSize: '1',
      category: 'Молочные',
      storageType: 'Холодильник',
      price: '95,5',
      supplier: 'Молочный двор',
      substitutes: [{ key: 'k', productId: 'cream', ratio: '0,5' }],
    })
  })

  it('в API — часы и базовые единицы', () => {
    expect(productFormToRequest({ ...productToForm(milk), shelfLife: '1,5', minStock: '0,5' })).toEqual({
      name: 'Молоко 3,2%',
      unit: 'Milliliter',
      defaultShelfLifeHours: 36,
      minStockQty: 500,
      packSize: 1000,
      category: 'Молочные',
      storageType: 'Холодильник',
      referencePrice: 95.5,
      supplier: 'Молочный двор',
      substitutes: [{ substituteId: 'cream', ratio: 0.5 }],
    })
  })

  it('штуки — без пересчёта; пустой срок — нормы нет', () => {
    expect(
      productFormToRequest({ name: 'Яйца', unit: 'Piece', shelfLife: '', shelfLifeUnit: 'days', minStock: '30', packSize: '10', category: '', storageType: '', price: '', supplier: '', substitutes: [] }),
    ).toEqual({ name: 'Яйца', unit: 'Piece', defaultShelfLifeHours: null, minStockQty: 30, packSize: 10, category: null, storageType: null, referencePrice: null, supplier: null, substitutes: [] })
  })

  it('проверка', () => {
    expect(validateProduct({ name: ' ', unit: 'Gram', shelfLife: '0', shelfLifeUnit: 'days', minStock: '-1', packSize: '0', category: '', storageType: '', price: '-1', supplier: '', substitutes: [{ key: 'a', productId: '', ratio: '0' }, { key: 'b', productId: 'x', ratio: '1' }, { key: 'c', productId: 'x', ratio: '11' }] })).toEqual({
      name: 'name',
      shelfLife: 'shelfLife',
      minStock: 'minStock',
      packSize: 'packSize',
      referencePrice: 'referencePrice',
      'a.product': 'product',
      'a.ratio': 'ratio',
      'c.product': 'duplicateSubstitute',
      'c.ratio': 'ratio',
    })
    expect(validateProduct(productToForm(milk))).toEqual({})
  })
})

describe('норма хранения короче суток', () => {
  it('часы — как есть; короткая норма показывается в часах', () => {
    const herbs: Product = { ...milk, name: 'Зелень', unit: 'Gram', defaultShelfLifeHours: 12 }
    expect(productToForm(herbs)).toMatchObject({ shelfLife: '12', shelfLifeUnit: 'hours' })
    expect(productFormToRequest({ ...productToForm(herbs), shelfLife: '8' }).defaultShelfLifeHours).toBe(8)
  })

  it('дробные часы — ошибка (сервер хранит целые часы)', () => {
    expect(validateProduct({ ...productToForm(milk), shelfLife: '1,5', shelfLifeUnit: 'hours' })).toEqual({ shelfLife: 'shelfLife' })
  })
})

describe('блюдо', () => {
  it('проверка: цена с копейками, состав без повторов и нулей', () => {
    const form = {
      ...emptyDishForm(),
      name: 'Блины',
      price: '320,555',
      ingredients: [
        { key: 'a', productId: 'milk', quantity: '200' },
        { key: 'b', productId: 'milk', quantity: '50' },
        { key: 'c', productId: '', quantity: '0' },
      ],
    }
    expect(validateDish(form)).toEqual({ price: 'price', 'b.product': 'duplicate', 'c.product': 'product', 'c.quantity': 'quantity' })
  })

  it('пустой состав — ошибка', () => {
    expect(validateDish({ ...emptyDishForm(), name: 'Чай', price: '100', ingredients: [] })).toEqual({ ingredients: 'empty' })
  })

  it('в API', () => {
    expect(
      dishFormToRequest({ name: ' Блины ', category: 'Завтраки', price: '320,5', isActive: true, ingredients: [{ key: 'a', productId: 'milk', quantity: '200' }] }),
    ).toEqual({ name: 'Блины', category: 'Завтраки', price: 320.5, isActive: true, ingredients: [{ productId: 'milk', quantity: 200 }] })
  })
})

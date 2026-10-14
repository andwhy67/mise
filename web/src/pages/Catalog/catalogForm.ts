import type { Product } from '@/api/types'
import type { MeasureUnit } from '@/lib/format'

/** Формы справочников — чистая логика: перевод в удобные единицы и обратно, проверка. */

const parse = (value: string) => (value.trim() === '' ? NaN : Number(value.replace(',', '.').replace(/\s/g, '')))
const show = (value: number) => String(Math.round(value * 1000) / 1000).replace('.', ',')
/** Граммы и миллилитры в форме — килограммы и литры: «держим не меньше 2 л», а не «2000 мл». */
const factor = (unit: MeasureUnit) => (unit === 'Piece' ? 1 : 1000)

export interface ProductForm {
  name: string
  unit: MeasureUnit
  /** Норма хранения в днях; пусто — нормы нет (срок указывают в каждой поставке). */
  shelfLifeDays: string
  minStock: string
  packSize: string
  category: string
  storageType: string
}

export const emptyProductForm = (): ProductForm => ({ name: '', unit: 'Gram', shelfLifeDays: '', minStock: '0', packSize: '1', category: '', storageType: '' })

export const productToForm = (p: Product): ProductForm => ({
  name: p.name,
  unit: p.unit,
  shelfLifeDays: p.defaultShelfLifeHours ? show(p.defaultShelfLifeHours / 24) : '',
  minStock: show(p.minStockQty / factor(p.unit)),
  packSize: show(p.packSize / factor(p.unit)),
  category: p.category ?? '',
  storageType: p.storageType ?? '',
})

export function productFormToRequest(form: ProductForm) {
  const days = parse(form.shelfLifeDays)
  return {
    name: form.name.trim(),
    unit: form.unit,
    defaultShelfLifeHours: Number.isNaN(days) ? null : Math.round(days * 24),
    minStockQty: Math.round(parse(form.minStock) * factor(form.unit) * 1000) / 1000,
    packSize: Math.round(parse(form.packSize) * factor(form.unit) * 1000) / 1000,
    category: form.category.trim() || null,
    storageType: form.storageType.trim() || null,
  }
}

export function validateProduct(form: ProductForm): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!form.name.trim()) errors.name = 'name'
  if (form.shelfLifeDays.trim() && !(parse(form.shelfLifeDays) * 24 >= 1)) errors.shelfLifeDays = 'shelfLife'
  if (!(parse(form.minStock) >= 0)) errors.minStock = 'minStock'
  if (!(parse(form.packSize) > 0)) errors.packSize = 'packSize'
  return errors
}

export interface IngredientRow {
  key: string
  productId: string
  /** На одну порцию, в базовой единице продукта (г, мл, шт). */
  quantity: string
}

export interface DishForm {
  name: string
  category: string
  price: string
  isActive: boolean
  ingredients: IngredientRow[]
}

export const emptyDishForm = (): DishForm => ({ name: '', category: '', price: '', isActive: true, ingredients: [] })

export function validateDish(form: DishForm): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!form.name.trim()) errors.name = 'name'
  const price = parse(form.price)
  if (!(price >= 0) || Math.round(price * 100) / 100 !== price) errors.price = 'price'
  if (form.ingredients.length === 0) errors.ingredients = 'empty'
  const seen = new Set<string>()
  for (const row of form.ingredients) {
    if (!row.productId) errors[`${row.key}.product`] = 'product'
    else if (seen.has(row.productId)) errors[`${row.key}.product`] = 'duplicate'
    seen.add(row.productId)
    if (!(parse(row.quantity) > 0)) errors[`${row.key}.quantity`] = 'quantity'
  }
  return errors
}

export const dishFormToRequest = (form: DishForm) => ({
  name: form.name.trim(),
  category: form.category.trim() || null,
  price: parse(form.price),
  isActive: form.isActive,
  ingredients: form.ingredients.map((row) => ({ productId: row.productId, quantity: parse(row.quantity) })),
})

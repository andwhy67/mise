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
  /** Норма хранения; пусто — нормы нет (срок указывают в каждой поставке). */
  shelfLife: string
  /** В чём указана норма: часы — для продуктов короче суток (зелень, выпечка, заготовки). */
  shelfLifeUnit: 'hours' | 'days'
  minStock: string
  packSize: string
  category: string
  storageType: string
  /** Ориентировочная цена закупки, ₽ за кг / л / шт; пусто — неизвестна. */
  price: string
  supplier: string
  substitutes: SubstituteRow[]
}

/** Замена при нехватке: ratio единиц заменителя на 1 единицу продукта (единицы одинаковые — «1» значит столько же). */
export interface SubstituteRow {
  key: string
  productId: string
  ratio: string
}

const MAX_RATIO = 10

export const emptyProductForm = (): ProductForm => ({
  name: '',
  unit: 'Gram',
  shelfLife: '',
  shelfLifeUnit: 'days',
  minStock: '0',
  packSize: '1',
  category: '',
  storageType: '',
  price: '',
  supplier: '',
  substitutes: [],
})

/** Норма в часах показывается часами, если она короче двух суток или не делится на целые дни. */
const inHours = (hours: number) => hours < 48 || hours % 24 !== 0

export const productToForm = (p: Product, key: () => string = () => crypto.randomUUID()): ProductForm => ({
  name: p.name,
  unit: p.unit,
  shelfLife: p.defaultShelfLifeHours ? show(inHours(p.defaultShelfLifeHours) ? p.defaultShelfLifeHours : p.defaultShelfLifeHours / 24) : '',
  shelfLifeUnit: p.defaultShelfLifeHours && inHours(p.defaultShelfLifeHours) ? 'hours' : 'days',
  minStock: show(p.minStockQty / factor(p.unit)),
  packSize: show(p.packSize / factor(p.unit)),
  category: p.category ?? '',
  storageType: p.storageType ?? '',
  price: p.referencePrice != null ? show(p.referencePrice) : '',
  supplier: p.supplier ?? '',
  substitutes: (p.substitutes ?? []).map((s) => ({ key: key(), productId: s.productId, ratio: show(s.ratio) })),
})

export function productFormToRequest(form: ProductForm) {
  const value = parse(form.shelfLife)
  return {
    name: form.name.trim(),
    unit: form.unit,
    defaultShelfLifeHours: Number.isNaN(value) ? null : Math.round(form.shelfLifeUnit === 'hours' ? value : value * 24),
    minStockQty: Math.round(parse(form.minStock) * factor(form.unit) * 1000) / 1000,
    packSize: Math.round(parse(form.packSize) * factor(form.unit) * 1000) / 1000,
    category: form.category.trim() || null,
    storageType: form.storageType.trim() || null,
    referencePrice: form.price.trim() ? parse(form.price) : null,
    supplier: form.supplier.trim() || null,
    substitutes: form.substitutes.map((s) => ({ substituteId: s.productId, ratio: parse(s.ratio) })),
  }
}

export function validateProduct(form: ProductForm): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!form.name.trim()) errors.name = 'name'
  if (form.shelfLife.trim()) {
    const value = parse(form.shelfLife)
    const hours = form.shelfLifeUnit === 'hours' ? value : value * 24
    // Часы — целые: сервер хранит норму в целых часах.
    if (!(hours >= 1) || (form.shelfLifeUnit === 'hours' && !Number.isInteger(value))) errors.shelfLife = 'shelfLife'
  }
  if (!(parse(form.minStock) >= 0)) errors.minStock = 'minStock'
  if (!(parse(form.packSize) > 0)) errors.packSize = 'packSize'
  if (form.price.trim() && !(parse(form.price) >= 0 && parse(form.price) <= 10_000_000)) errors.referencePrice = 'referencePrice'
  const seen = new Set<string>()
  for (const row of form.substitutes) {
    if (!row.productId) errors[`${row.key}.product`] = 'product'
    else if (seen.has(row.productId)) errors[`${row.key}.product`] = 'duplicateSubstitute'
    seen.add(row.productId)
    const ratio = parse(row.ratio)
    if (!(ratio > 0 && ratio <= MAX_RATIO)) errors[`${row.key}.ratio`] = 'ratio'
  }
  return errors
}

export interface IngredientRow {
  key: string
  productId: string
  /** На одну порцию, в базовой единице продукта (г, мл, шт). */
  quantity: string
  /** Можно убрать по просьбе гостя или когда кончился (гарнир, посыпка). */
  removable?: boolean
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
  ingredients: form.ingredients.map((row) => ({ productId: row.productId, quantity: parse(row.quantity), removable: row.removable ?? false })),
})

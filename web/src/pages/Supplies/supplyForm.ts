import type { Product } from '@/api/types'

/**
 * Форма приёма поставки — чистая логика: предпросмотр срока, проверка и запрос к API.
 * Время в полях — московское (DESIGN.md §1.1; в Москве нет перехода на летнее время, смещение постоянное).
 */
const MOSCOW_OFFSET = '+03:00'
const HOUR_MS = 3_600_000
const NUMBER_PATTERN = /^[A-Za-zА-Яа-яЁё0-9][A-Za-zА-Яа-яЁё0-9._/-]*$/

export type ExpiryMode = 'default' | 'hours' | 'days' | 'date'
export type InputUnit = 'Gram' | 'Kilogram' | 'Milliliter' | 'Liter' | 'Piece'

export interface SupplyRow {
  key: string
  productId: string
  quantity: string
  unit: InputUnit | null
  expiry: ExpiryMode
  /** «Хранить N часов» (целое, от 1) или «N дней» (допускается «1,5»). */
  amount: string
  /** «Годен до»: YYYY-MM-DDTHH:mm по Москве — ровно этот момент; только дата — до конца этого дня. */
  date: string
}

export interface SupplyFormState {
  number: string
  supplier: string
  /** Дата и время поставки, YYYY-MM-DDTHH:mm по Москве. */
  createdAt: string
  rows: SupplyRow[]
}

export const emptyRow = (key: string): SupplyRow => ({ key, productId: '', quantity: '', unit: null, expiry: 'default', amount: '', date: '' })

export const fromMoscowInput = (value: string) => new Date(`${value.length === 16 ? `${value}:00` : value}${MOSCOW_OFFSET}`)

export function toMoscowInput(date: Date): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Moscow',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(date)
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? ''
  return `${get('year')}-${get('month')}-${get('day')}T${get('hour')}:${get('minute')}`
}

/** «Годен до 17 октября» — весь этот день, до 23:59 по Москве; «до 17 октября 18:30» — ровно до этого момента. */
const expiryMoment = (value: string) => fromMoscowInput(value.length === 10 ? `${value}T23:59` : value)

/** Срок хранения в часах из поля формы: часы — целые от 1, дни — с переводом в часы. */
function shelfLifeHours(row: SupplyRow): number | null {
  const amount = parseNumber(row.amount)
  if (!(amount > 0)) return null
  const hours = row.expiry === 'hours' ? amount : Math.round(amount * 24)
  return Number.isInteger(hours) && hours >= 1 ? hours : null
}

const parseNumber = (value: string) => (value.trim() === '' ? NaN : Number(value.replace(',', '.').replace(/\s/g, '')))

/** Единицы ввода для продукта: на кухне считают и в кг, и в граммах — сервер переведёт в базовую. */
export const unitsFor = (product: Product | undefined): InputUnit[] =>
  product?.unit === 'Gram' ? ['Kilogram', 'Gram'] : product?.unit === 'Milliliter' ? ['Liter', 'Milliliter'] : ['Piece']

/**
 * Строка поставки из подсказки «Что заказать»: количество приходит в базовой единице продукта (г, мл, шт).
 * Вес и объём показываем в кг и л, штуки — штуками. Продукта нет в справочнике — строки нет: угадывать единицу нельзя.
 */
export function rowFromSuggestion(key: string, product: Product | undefined, baseQuantity: number): SupplyRow | null {
  if (!product) return null
  const unit = unitsFor(product)[0]!
  const big = unit === 'Kilogram' || unit === 'Liter'
  const shown = big ? baseQuantity / 1000 : baseQuantity
  return { ...emptyRow(key), productId: product.id, unit, quantity: String(Math.round(shown * 1000) / 1000).replace('.', ',') }
}

export interface ExpiryPreview {
  expiresAt: Date | null
  source: 'explicit' | 'shelfLife' | 'default' | null
}

/** Срок годности строки по тем же правилам, что на сервере: явная дата → срок хранения → норма продукта. */
export function previewExpiry(row: SupplyRow, product: Product | undefined, createdAt: string): ExpiryPreview {
  const received = fromMoscowInput(createdAt)
  if (row.expiry === 'date' && row.date) return { expiresAt: expiryMoment(row.date), source: 'explicit' }
  if (row.expiry === 'hours' || row.expiry === 'days') {
    const hours = shelfLifeHours(row)
    return hours ? { expiresAt: new Date(received.getTime() + hours * HOUR_MS), source: 'shelfLife' } : { expiresAt: null, source: null }
  }
  if (product?.defaultShelfLifeHours) return { expiresAt: new Date(received.getTime() + product.defaultShelfLifeHours * HOUR_MS), source: 'default' }
  return { expiresAt: null, source: null }
}

/** Ошибки по ключам «<строка>.<поле>» (или полю формы); значение — код текста в переводах. */
export type SupplyErrors = Record<string, string>

export function validateSupply(form: SupplyFormState, products: Product[]): SupplyErrors {
  const errors: SupplyErrors = {}
  if (!NUMBER_PATTERN.test(form.number.trim())) errors.number = 'number'
  if (!form.createdAt || Number.isNaN(fromMoscowInput(form.createdAt).getTime())) errors.createdAt = 'createdAt'
  if (form.rows.length === 0) errors.rows = 'empty'

  for (const row of form.rows) {
    const product = products.find((p) => p.id === row.productId)
    if (!product) errors[`${row.key}.product`] = 'product'
    if (product && (!row.unit || !unitsFor(product).includes(row.unit))) errors[`${row.key}.unit`] = 'unit'
    const quantity = parseNumber(row.quantity)
    if (!(quantity > 0)) errors[`${row.key}.quantity`] = 'quantity'
    if ((row.expiry === 'hours' || row.expiry === 'days') && shelfLifeHours(row) === null) errors[`${row.key}.amount`] = row.expiry
    if (row.expiry === 'date') {
      if (!row.date) errors[`${row.key}.date`] = 'date'
      else if (!errors.createdAt && expiryMoment(row.date) <= fromMoscowInput(form.createdAt)) errors[`${row.key}.date`] = 'dateBeforeDelivery'
    }
    if (product && row.expiry === 'default' && !product.defaultShelfLifeHours) errors[`${row.key}.expiry`] = 'noShelfLife'
  }
  return errors
}

export interface SupplyRequestItem {
  productId: string
  quantity: number
  unit: InputUnit
  shelfLifeHours?: number
  expiresAt?: string
}

export function toRequest(form: SupplyFormState) {
  return {
    number: form.number.trim(),
    supplier: form.supplier.trim() || undefined,
    createdAt: fromMoscowInput(form.createdAt).toISOString(),
    items: form.rows.map((row): SupplyRequestItem => {
      const item: SupplyRequestItem = { productId: row.productId, quantity: parseNumber(row.quantity), unit: row.unit ?? 'Piece' }
      if (row.expiry === 'hours' || row.expiry === 'days') item.shelfLifeHours = shelfLifeHours(row) ?? undefined
      if (row.expiry === 'date') item.expiresAt = expiryMoment(row.date).toISOString()
      return item
    }),
  }
}

/** Номер по умолчанию: SUP-20261014-1530 — понятный, уникальный в пределах минуты; можно поправить. */
export const defaultNumber = (now: Date) => `SUP-${toMoscowInput(now).replace(/[-:]/g, '').replace('T', '-')}`

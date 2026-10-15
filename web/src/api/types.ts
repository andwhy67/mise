import type { MeasureUnit, ShelfStatus } from '@/lib/format'
import type { UserRole } from '@/auth/session'

// Ответы API (только поля, которые нужны интерфейсу). Даты — ISO-строки в UTC.

export interface IngredientAvailability {
  productId: string
  productName: string
  unit: MeasureUnit
  qtyPerPortion: number
  usable: number
  portions: number
}

export interface StopListItem {
  dishId: string
  dishName: string
  category: string | null
  reason: 'Shortage' | 'Manual'
  limitingProduct: IngredientAvailability | null
  since: string | null
}

export interface StopListForecastItem {
  dishId: string
  dishName: string
  eta: string
  hoursLeft: number
  limitingProduct: { productId: string; productName: string; unit: MeasureUnit; usableQty: number; ratePerHour: number }
  qtyPerPortion: number
  cause: 'Consumption' | 'Expiry'
  confidence: 'Low' | 'Medium' | 'High'
}

export interface StopListForecast {
  generatedAt: string
  horizonHours: number
  items: StopListForecastItem[]
}

export interface HotOfferReason {
  productId: string
  productName: string
  unit: MeasureUnit
  atRiskQty: number
  unsoldQty: number | null
  hoursLeft: number
  expiresAt: string
  qtyPerPortion: number
  portionsBurned: number
  pressure: number
  share: number
  contribution: number
}

export interface HotOffer {
  rank: number
  dishId: string
  dishName: string
  maxPortions: number
  score: number
  reasons: HotOfferReason[]
}

export interface HotOffers {
  generatedAt: string
  scorer: string
  offers: HotOffer[]
}

export interface Batch {
  id: string
  productId: string
  productName: string
  unit: MeasureUnit
  supplyId: string
  supplyNumber: string
  receivedAt: string
  expiresAt: string
  initialQty: number
  remainingQty: number
  status: 'Active' | 'Depleted' | 'Expired' | 'WrittenOff'
  isUsable: boolean
  shelfLife: { status: ShelfStatus; hoursLeft: number; remainingFraction: number }
  /** Очередь списания среди пригодных: 1 — уйдёт первой. */
  fefoRank: number | null
}

export interface Kpi {
  now: { dishesInStop: number; batchesExpiringWithin24h: number; productsBelowMinimum: number; writeOffsToday: number; ordersToday: number; revenueToday: number }
}

export interface SupplySuggestion {
  productId: string
  productName: string
  unit: MeasureUnit
  usableQty: number
  suggestedQty: number
  expectedWaste: number
  urgency: 'Normal' | 'High' | 'Critical'
  runsOutAt: string | null
  confidence: 'Low' | 'Medium' | 'High'
  expectedDemand: number
}

export interface SupplySuggestions {
  generatedAt: string
  horizonHours: number
  items: SupplySuggestion[]
}

export interface AuthConfig {
  demoEnabled: boolean
  demoRoles: Exclude<UserRole, 'Simulator'>[]
}

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export type StockStatus = 'Fresh' | 'Warning' | 'Critical' | 'OutOfStock'

export interface StockItem {
  productId: string
  productName: string
  unit: MeasureUnit
  category: string | null
  usableQty: number
  totalQty: number
  expiredQty: number
  minStockQty: number
  belowMinStock: boolean
  usableBatchCount: number
  nearestExpiresAt: string | null
  nearestHoursLeft: number | null
  status: StockStatus
}

export interface ProductBatches {
  stock: StockItem
  batches: Batch[]
}

export interface WriteOffRequest {
  quantity?: number
  /** Единица ввода (Kilogram, Gram, Liter…); без неё — базовая единица продукта. */
  unit?: string
  reason: string
}

export interface DishAvailability {
  dishId: string
  dishName: string
  category: string | null
  price: number
  isActive: boolean
  maxPortions: number
  status: 'Available' | 'StopShortage' | 'StopManual'
  inStopList: boolean
  limitingProduct: IngredientAvailability | null
  ingredients: IngredientAvailability[]
}

export interface StopListHistoryItem {
  id: string
  dishId: string
  dishName: string
  reason: 'Shortage' | 'Manual'
  limitingProductId: string | null
  limitingProductName: string | null
  startedAt: string
  endedAt: string | null
  durationMinutes: number | null
}

export type OrderStatus = 'Created' | 'Cooking' | 'Done' | 'Served' | 'Cancelled'

export interface OrderListItem {
  id: string
  number: string
  status: OrderStatus
  createdAt: string
  portions: number
  total: number
}

export interface Order {
  id: string
  number: string
  status: OrderStatus
  createdAt: string
  updatedAt: string
  cancelledAt: string | null
  total: number
  items: { lineNo: number; dishId: string; dishName: string; quantity: number; price: number; amount: number }[]
  consumption: { productId: string; productName: string; unit: MeasureUnit; batchId: string; batchExpiresAt: string; consumed: number; returned: number }[]
  /** Замены, на которые согласился повар. */
  substitutions: { productId: string; productName: string; substituteId: string; substituteName: string; unit: MeasureUnit; ratio: number }[]
}

/** Чего не хватило для заказа — расширение `missing` в ответе 422. */
export interface MissingIngredient {
  productId: string
  productName: string
  unit: MeasureUnit
  required: number
  available: number
  shortage: number
  dishes: string[]
  /** Чем можно заменить (правила справочника); required — сколько заменителя нужно всему заказу. */
  substitutes: { productId: string; productName: string; unit: MeasureUnit; ratio: number; required: number; available: number; enough: boolean }[]
}

export type AlertType = 'ExpiringSoon' | 'Expired' | 'LowStock' | 'StopListOn' | 'StopListOff'
export type AlertSeverity = 'Info' | 'Warning' | 'Critical'

export interface Alert {
  id: string
  type: AlertType
  severity: AlertSeverity
  message: string
  batchId: string | null
  productId: string | null
  productName: string | null
  dishId: string | null
  dishName: string | null
  createdAt: string
  acknowledgedAt: string | null
  resolvedAt: string | null
  isActive: boolean
  unit: MeasureUnit | null
  quantity: number | null
  limitQty: number | null
  expiresAt: string | null
  autoWrittenOff: boolean | null
}

export interface AlertSummary {
  total: number
  critical: number
  warning: number
  info: number
}

export interface Dish {
  id: string
  name: string
  category: string | null
  price: number
  isActive: boolean
  ingredients: { productId: string; productName: string; unit: MeasureUnit; quantity: number }[]
}

export interface Product {
  id: string
  name: string
  unit: MeasureUnit
  defaultShelfLifeHours: number | null
  minStockQty: number
  packSize: number
  category: string | null
  storageType: string | null
  /** Ориентировочная цена закупки, ₽ за кг / л / шт. */
  referencePrice: number | null
  supplier: string | null
  /** Чем можно заменить при нехватке: ratio единиц заменителя на 1 единицу продукта (базовые единицы). */
  substitutes: { productId: string; productName: string; unit: MeasureUnit; ratio: number }[]
}

/** Себестоимость порции по партиям, которые уйдут в работу первыми (FEFO), и маржа блюда. */
export interface DishEconomics {
  dishId: string
  dishName: string
  category: string | null
  price: number
  /** null — у какого-то продукта нет ни цены партии, ни справочной. */
  portionCost: number | null
  margin: number | null
  /** Доля себестоимости в цене, 0…1. */
  foodCostShare: number | null
  /** Часть цены — справочная, а не из партий. */
  estimated: boolean
  unpricedProducts: string[]
}

/** «Что докупить, чтобы вернуть блюда из стопа». */
export interface Restock {
  now: string
  horizonHours: number
  minPortions: number
  dishes: {
    dishId: string
    dishName: string
    category: string | null
    targetPortions: number
    portionsPerDay: number
    lines: { productId: string; productName: string; unit: MeasureUnit; perPortion: number; needed: number; usable: number; missing: number }[]
    explanation: string
  }[]
  purchases: {
    productId: string
    productName: string
    unit: MeasureUnit
    supplier: string | null
    packSize: number
    missing: number
    toBuy: number
    /** Названия блюд, которые вернёт закупка. */
    dishes: string[]
    explanation: string
  }[]
}

/** Заявка поставщику: номер и ориентировочная сумма (статусов нет — отправляет человек). */
export interface SupplyRequest {
  number: string
  createdAt: string
  supplier: string | null
  lines: { productId: string; productName: string; unit: MeasureUnit; quantity: number; price: number | null; cost: number | null }[]
  estimatedTotal: number
  unpricedLines: number
}

export interface SupplyListItem {
  id: string
  number: string
  createdAt: string
  registeredAt: string
  supplier: string | null
  itemCount: number
}

export interface Supply {
  id: string
  number: string
  createdAt: string
  registeredAt: string
  supplier: string | null
  items: {
    id: string
    lineNo: number
    productId: string
    productName: string
    unit: MeasureUnit
    quantity: number
    shelfLifeHours: number | null
    requestedExpiresAt: string | null
    batchId: string
    expiresAt: string
    expirySource: string
    batchStatus: 'Active' | 'Depleted' | 'Expired' | 'WrittenOff'
    /** ₽ за кг / л / шт; null — цены нет. */
    price: number | null
    lineCost: number | null
  }[]
}

export interface User {
  id: string
  login: string
  displayName: string
  role: UserRole
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

export type MenuGroup = 'Saves' | 'Overstock' | 'Regular'

export interface StockCover {
  productId: string
  productName: string
  unit: MeasureUnit
  usableQty: number
  dailyConsumption: number
  coverDays: number
}

export interface HotMenuItem {
  rank: number
  dishId: string
  dishName: string
  category: string | null
  price: number
  maxPortions: number
  group: MenuGroup
  offer: HotOffer | null
  cover: StockCover | null
}

export interface HotMenu {
  generatedAt: string
  scorer: string
  items: HotMenuItem[]
}

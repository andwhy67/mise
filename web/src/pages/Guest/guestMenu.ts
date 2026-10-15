import type { GuestDish, GuestMenu } from '@/api/guest'
import type { DishAvailability } from '@/api/types'
import { describeChanges, parseLine, type Cart, type LineChange } from '@/pages/Orders/cart'

/**
 * Гостевое меню — чистая логика. Сервер не отдаёт гостю остатки, только «закончилось / есть», поэтому
 * для общего окна «Изменить блюдо» (DishCustomizer) блюдо переводится в форму меню повара с условными
 * количествами: порция = 1, есть = 1, закончилось = 0.
 */
export function toAvailability(dish: GuestDish): DishAvailability {
  const available = dish.state === 'Available'
  const names = new Map(dish.ingredients.flatMap((i) => [[i.productId, i.name] as const, ...i.substitutes.map((s) => [s.productId, s.name] as const)]))
  return {
    dishId: dish.id,
    dishName: dish.name,
    category: dish.category,
    price: dish.price,
    isActive: true,
    maxPortions: available ? 1 : 0,
    status: available ? 'Available' : 'StopShortage',
    inStopList: !available,
    limitingProduct: null,
    ingredients: dish.ingredients.map((i) => ({
      productId: i.productId,
      productName: i.name,
      unit: 'Piece',
      qtyPerPortion: 1,
      usable: i.out ? 0 : 1,
      portions: i.out ? 0 : 1,
      removable: i.removable,
      substitutes: i.substitutes.map((s) => ({ productId: s.productId, productName: s.name, unit: 'Piece', ratio: 1, usable: 1 })),
    })),
    alternative:
      dish.suggestedChanges.length > 0
        ? {
            portions: 1,
            changes: dish.suggestedChanges.map((c) => ({
              productId: c.productId,
              productName: names.get(c.productId) ?? '?',
              kind: c.kind,
              substituteId: c.substituteId,
              substituteName: c.substituteId ? (names.get(c.substituteId) ?? '?') : null,
              ratio: 1,
            })),
          }
        : null,
  }
}

type Translate = (key: string, options?: Record<string, unknown>) => string

/** «без лимона · шпинат → руккола» (переводы — из orders:change). */
export function changeText(dish: GuestDish, changes: LineChange[], t: Translate) {
  return describeChanges(toAvailability(dish), changes)
    .map((c) => (c.kind === 'Remove' ? t('orders:change.remove', { product: c.productName }) : t('orders:change.replace', { product: c.productName, substitute: c.substituteName })))
    .join(' · ')
}

/** Изменения, с которыми блюдо из стопа можно подать (вариант сервера). */
export const suggestedChanges = (dish: GuestDish): LineChange[] =>
  dish.suggestedChanges.map((c) => (c.kind === 'Remove' ? { productId: c.productId, kind: 'Remove' } : { productId: c.productId, kind: 'Replace', substituteId: c.substituteId! }))

/** Можно ли что-то поменять в блюде: убрать ингредиент или заменить его. */
export const isGuestCustomizable = (dish: GuestDish) => dish.ingredients.some((i) => i.removable || i.substitutes.length > 0)

/** Разделы меню в порядке сервера; блюда без категории — в конце, «Другое». */
export function byCategory(dishes: GuestDish[]): { category: string | null; dishes: GuestDish[] }[] {
  const groups = new Map<string | null, GuestDish[]>()
  for (const dish of dishes) groups.set(dish.category, [...(groups.get(dish.category) ?? []), dish])
  return [...groups.entries()].map(([category, list]) => ({ category, dishes: list }))
}

export interface GuestCartLine {
  key: string
  dish: GuestDish
  quantity: number
  changes: LineChange[]
}

/** Строки корзины, которые ещё есть в меню (блюдо могли убрать, пока гость выбирал). */
export function cartLines(cart: Cart, menu: GuestMenu | undefined): GuestCartLine[] {
  if (!menu) return []
  return [...cart.entries()].flatMap(([key, quantity]) => {
    const { dishId, changes } = parseLine(key)
    const dish = menu.dishes.find((d) => d.id === dishId)
    return dish && quantity > 0 ? [{ key, dish, quantity, changes }] : []
  })
}

export const cartCount = (lines: GuestCartLine[]) => lines.reduce((sum, l) => sum + l.quantity, 0)
export const cartTotal = (lines: GuestCartLine[]) => lines.reduce((sum, l) => sum + l.quantity * l.dish.price, 0)

/** Строка без изменений нельзя заказать, если блюдо закончилось (с изменениями — решит сервер). */
export const lineBlocked = (line: GuestCartLine) =>
  line.dish.state === 'Unavailable' || (line.dish.state === 'WithChanges' && line.changes.length === 0)

/** Номер стола из QR (?t=5): только целое в пределах столов ресторана. */
export function tableFromQuery(value: string | null, tables: number): number | null {
  if (!value || !/^\d{1,3}$/.test(value)) return null
  const table = Number(value)
  return table >= 1 && table <= tables ? table : null
}

// ---------- Корзина на устройстве гостя: перезагрузка страницы не теряет выбор ----------

const cartStorageKey = (menuKey: string) => `mise.guest.cart.${menuKey}`

export function loadCart(menuKey: string): Cart {
  try {
    const raw = localStorage.getItem(cartStorageKey(menuKey))
    const entries = raw ? (JSON.parse(raw) as [string, number][]) : []
    return new Map(entries.filter(([key, quantity]) => typeof key === 'string' && Number.isInteger(quantity) && quantity > 0))
  } catch {
    return new Map()
  }
}

export function saveCart(menuKey: string, cart: Cart) {
  try {
    if (cart.size === 0) localStorage.removeItem(cartStorageKey(menuKey))
    else localStorage.setItem(cartStorageKey(menuKey), JSON.stringify([...cart.entries()]))
  } catch {
    // Приватный режим или запрет хранилища — корзина просто живёт до перезагрузки.
  }
}

/** Последний заказ гостя в этом меню — ссылка «Мой заказ» в шапке. */
const lastOrderKey = (menuKey: string) => `mise.guest.order.${menuKey}`

export function rememberOrder(menuKey: string, token: string) {
  try {
    localStorage.setItem(lastOrderKey(menuKey), token)
  } catch {
    // см. saveCart
  }
}

export function lastOrder(menuKey: string): string | null {
  try {
    return localStorage.getItem(lastOrderKey(menuKey))
  } catch {
    return null
  }
}

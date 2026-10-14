import type { DishAvailability, MissingIngredient } from '@/api/types'

/** Корзина: блюдо → сколько порций. */
export type Cart = Map<string, number>

/**
 * Чего не хватит на весь заказ сразу — по сумме всех блюд (общие ингредиенты складываются, CLAUDE.md, правило 4).
 * Это подсказка до отправки; окончательно решает сервер (422 с тем же форматом `missing`).
 */
export function cartShortages(cart: Cart, menu: DishAvailability[]): MissingIngredient[] {
  const needs = new Map<string, MissingIngredient>()
  for (const [dishId, quantity] of cart) {
    const dish = menu.find((d) => d.dishId === dishId)
    if (!dish || quantity <= 0) continue
    for (const ingredient of dish.ingredients) {
      const need = needs.get(ingredient.productId) ?? {
        productId: ingredient.productId,
        productName: ingredient.productName,
        unit: ingredient.unit,
        required: 0,
        available: ingredient.usable,
        shortage: 0,
        dishes: [],
      }
      need.required += ingredient.qtyPerPortion * quantity
      need.dishes.push(dish.dishName)
      needs.set(ingredient.productId, need)
    }
  }
  return [...needs.values()]
    .filter((need) => need.required > need.available)
    .map((need) => ({ ...need, shortage: need.required - need.available }))
}

export interface ProductUsage {
  productId: string
  productName: string
  unit: DishAvailability['ingredients'][number]['unit']
  required: number
  available: number
  /** Какую долю доступного остатка заберёт заказ: 0,7 — 70 %, больше 1 — не хватит. */
  share: number
}

/** Сколько каждого продукта заберёт корзина целиком — чтобы видеть запас, а не только «хватит/не хватит». */
export function cartUsage(cart: Cart, menu: DishAvailability[]): ProductUsage[] {
  const usage = new Map<string, ProductUsage>()
  for (const [dishId, quantity] of cart) {
    const dish = menu.find((d) => d.dishId === dishId)
    if (!dish || quantity <= 0) continue
    for (const ingredient of dish.ingredients) {
      const current = usage.get(ingredient.productId) ?? {
        productId: ingredient.productId,
        productName: ingredient.productName,
        unit: ingredient.unit,
        required: 0,
        available: ingredient.usable,
        share: 0,
      }
      current.required += ingredient.qtyPerPortion * quantity
      usage.set(ingredient.productId, current)
    }
  }
  return [...usage.values()]
    .map((u) => ({ ...u, share: u.available > 0 ? Math.round((u.required / u.available) * 1000) / 1000 : Infinity }))
    .sort((a, b) => b.share - a.share || a.productName.localeCompare(b.productName))
}

export function cartTotals(cart: Cart, menu: DishAvailability[]) {
  let portions = 0
  let total = 0
  let lines = 0
  for (const [dishId, quantity] of cart) {
    const dish = menu.find((d) => d.dishId === dishId)
    if (!dish || quantity <= 0) continue
    portions += quantity
    total += dish.price * quantity
    lines += 1
  }
  return { portions, total, lines }
}

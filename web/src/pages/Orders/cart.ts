import type { DishAvailability, MissingIngredient } from '@/api/types'
import type { MeasureUnit } from '@/lib/format'

/** Корзина: блюдо → сколько порций. */
export type Cart = Map<string, number>

/** Замена, на которую согласился повар: весь продукт заказа берётся из заменителя × ratio. */
export interface CartSubstitution {
  productId: string
  productName: string
  substituteId: string
  substituteName: string
  /** Единица заменителя. */
  unit: MeasureUnit
  ratio: number
}

/** Пригодный остаток продуктов, известный из меню (все ингредиенты всех блюд). */
function usableFromMenu(menu: DishAvailability[]) {
  const usable = new Map<string, number>()
  for (const dish of menu) for (const i of dish.ingredients) usable.set(i.productId, i.usable)
  return usable
}

/** Какие продукты использует корзина — замену отправляем только для них (иначе сервер ответит 400). */
export function activeSubstitutions(cart: Cart, menu: DishAvailability[], substitutions: CartSubstitution[]) {
  const used = new Set<string>()
  for (const [dishId, quantity] of cart) {
    if (quantity <= 0) continue
    for (const i of menu.find((d) => d.dishId === dishId)?.ingredients ?? []) used.add(i.productId)
  }
  return substitutions.filter((s) => used.has(s.productId))
}

interface Need {
  productId: string
  productName: string
  unit: MeasureUnit
  required: number
  available: number
  dishes: string[]
}

/**
 * Потребность корзины по продуктам — сумма по всем блюдам; замены переносят её на заменитель (× ratio),
 * как на сервере. Остаток заменителя, которого нет ни в одном блюде меню, неизвестен (NaN) — решит сервер.
 */
function cartNeeds(cart: Cart, menu: DishAvailability[], substitutions: CartSubstitution[]): Need[] {
  const needs = new Map<string, Need>()
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
        dishes: [],
      }
      need.required += ingredient.qtyPerPortion * quantity
      if (!need.dishes.includes(dish.dishName)) need.dishes.push(dish.dishName)
      needs.set(ingredient.productId, need)
    }
  }
  const usable = usableFromMenu(menu)
  for (const s of substitutions) {
    const original = needs.get(s.productId)
    if (!original) continue
    needs.delete(s.productId)
    const target = needs.get(s.substituteId) ?? {
      productId: s.substituteId,
      productName: s.substituteName,
      unit: s.unit,
      required: 0,
      available: usable.get(s.substituteId) ?? NaN,
      dishes: [],
    }
    target.required += original.required * s.ratio
    for (const name of original.dishes) if (!target.dishes.includes(name)) target.dishes.push(name)
    needs.set(s.substituteId, target)
  }
  return [...needs.values()]
}

/**
 * Чего не хватит на весь заказ сразу — по сумме всех блюд (общие ингредиенты складываются, CLAUDE.md, правило 4).
 * Это подсказка до отправки; окончательно решает сервер (422 с тем же форматом `missing`).
 */
export function cartShortages(cart: Cart, menu: DishAvailability[], substitutions: CartSubstitution[] = []): MissingIngredient[] {
  return cartNeeds(cart, menu, substitutions)
    .filter((need) => need.required > need.available)
    .map((need) => ({ ...need, shortage: need.required - need.available, substitutes: [] }))
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
export function cartUsage(cart: Cart, menu: DishAvailability[], substitutions: CartSubstitution[] = []): ProductUsage[] {
  return cartNeeds(cart, menu, substitutions)
    .filter((u) => !Number.isNaN(u.available))
    .map(({ productId, productName, unit, required, available }) => ({
      productId,
      productName,
      unit,
      required,
      available,
      share: available > 0 ? Math.round((required / available) * 1000) / 1000 : Infinity,
    }))
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

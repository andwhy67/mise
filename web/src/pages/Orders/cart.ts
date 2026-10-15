import type { DishAvailability, MissingIngredient } from '@/api/types'
import type { MeasureUnit } from '@/lib/format'

/**
 * Корзина: строка → сколько порций. Ключ строки — блюдо и его изменения: «dishId» — как в меню,
 * «dishId|-лимон|~шпинат>руккола» — без лимона и с заменой шпината. Одно блюдо может лежать несколькими строками.
 */
export type Cart = Map<string, number>

/** Изменение строки: убрать продукт или заменить его заменителем из правил справочника. */
export type LineChange = { productId: string; kind: 'Remove' } | { productId: string; kind: 'Replace'; substituteId: string }

/** Ключ строки: изменения в порядке productId — одинаковые наборы дают одну строку. */
export function lineKey(dishId: string, changes: LineChange[]): string {
  if (changes.length === 0) return dishId
  const parts = [...changes]
    .sort((a, b) => a.productId.localeCompare(b.productId))
    .map((c) => (c.kind === 'Remove' ? `-${c.productId}` : `~${c.productId}>${c.substituteId}`))
  return `${dishId}|${parts.join('|')}`
}

export function parseLine(key: string): { dishId: string; changes: LineChange[] } {
  const [dishId, ...parts] = key.split('|')
  const changes = parts.map((part): LineChange => {
    if (part.startsWith('-')) return { productId: part.slice(1), kind: 'Remove' }
    const [productId, substituteId] = part.slice(1).split('>')
    return { productId: productId!, kind: 'Replace', substituteId: substituteId! }
  })
  return { dishId: dishId!, changes }
}

/** «без лимона», «шпинат → руккола» — с именами из меню (для корзины и подписей). */
export function describeChanges(dish: DishAvailability | undefined, changes: LineChange[]) {
  return changes.map((c) => {
    const ingredient = dish?.ingredients.find((i) => i.productId === c.productId)
    const productName = ingredient?.productName ?? '?'
    if (c.kind === 'Remove') return { productId: c.productId, kind: c.kind, productName, substituteName: null }
    const option = ingredient?.substitutes.find((s) => s.productId === c.substituteId)
    return { productId: c.productId, kind: c.kind, productName, substituteName: option?.productName ?? '?' }
  })
}

/** Строки корзины для запроса: блюдо, порции, изменения. */
export function orderItems(cart: Cart) {
  return [...cart.entries()]
    .filter(([, quantity]) => quantity > 0)
    .map(([key, quantity]) => {
      const { dishId, changes } = parseLine(key)
      return {
        dishId,
        quantity,
        ...(changes.length
          ? { modifications: changes.map((c) => (c.kind === 'Remove' ? { productId: c.productId, kind: c.kind } : { ...c })) }
          : {}),
      }
    })
}

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

/** Пригодный остаток продуктов, известный из меню: ингредиенты всех блюд и их заменители. */
function usableFromMenu(menu: DishAvailability[]) {
  const usable = new Map<string, number>()
  for (const dish of menu)
    for (const i of dish.ingredients) {
      usable.set(i.productId, i.usable)
      for (const s of i.substitutes ?? []) if (!usable.has(s.productId)) usable.set(s.productId, s.usable)
    }
  return usable
}

/** Какие продукты использует корзина — замену отправляем только для них (иначе сервер ответит 400). */
export function activeSubstitutions(cart: Cart, menu: DishAvailability[], substitutions: CartSubstitution[]) {
  const used = new Set<string>()
  for (const [key, quantity] of cart) {
    const { dishId } = parseLine(key)
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
  const usable = usableFromMenu(menu)
  const add = (productId: string, productName: string, unit: MeasureUnit, available: number, qty: number, dishName: string) => {
    const need = needs.get(productId) ?? { productId, productName, unit, required: 0, available, dishes: [] }
    need.required += qty
    if (!need.dishes.includes(dishName)) need.dishes.push(dishName)
    needs.set(productId, need)
  }
  for (const [key, quantity] of cart) {
    const { dishId, changes } = parseLine(key)
    const dish = menu.find((d) => d.dishId === dishId)
    if (!dish || quantity <= 0) continue
    for (const ingredient of dish.ingredients) {
      const change = changes.find((c) => c.productId === ingredient.productId)
      const qty = ingredient.qtyPerPortion * quantity
      // Как на сервере: убранное не нужно, заменённое берётся из заменителя × коэффициент.
      if (change?.kind === 'Remove') continue
      if (change?.kind === 'Replace') {
        const option = ingredient.substitutes.find((s) => s.productId === change.substituteId)
        if (option) add(option.productId, option.productName, option.unit, usable.get(option.productId) ?? option.usable, qty * option.ratio, dish.dishName)
        continue
      }
      add(ingredient.productId, ingredient.productName, ingredient.unit, ingredient.usable, qty, dish.dishName)
    }
  }
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
  for (const [key, quantity] of cart) {
    const dish = menu.find((d) => d.dishId === parseLine(key).dishId)
    if (!dish || quantity <= 0) continue
    portions += quantity
    total += dish.price * quantity
    lines += 1
  }
  return { portions, total, lines }
}

/** Изменения, при которых блюдо можно приготовить: каждый недостающий продукт убран или заменён. */
export function missingWithout(dish: DishAvailability, changes: LineChange[]) {
  return dish.ingredients
    .filter((i) => i.usable < i.qtyPerPortion)
    .filter((i) => !changes.some((c) => c.productId === i.productId))
    .map((i) => i.productName)
}

/** Заменители, которых хватит хотя бы на порцию. */
export const fittingSubstitutes = (ingredient: DishAvailability['ingredients'][number]) =>
  ingredient.substitutes.filter((s) => s.usable >= ingredient.qtyPerPortion * s.ratio)

/** Есть ли что менять в блюде: убрать гарнир или заменить продукт. */
export const isCustomizable = (dish: DishAvailability) =>
  dish.ingredients.some((i) => i.removable || fittingSubstitutes(i).length > 0)

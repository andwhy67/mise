import { expect, test } from '@playwright/test'
import { apiAs, createDish, createProduct, receive, signIn } from './helpers'

/**
 * Главный сценарий кухни (CLAUDE.md, правило 4): проверка заказа — суммарная по всем блюдам.
 * Два блюда делят один продукт: по отдельности хватает, вместе — нет. Отказ показывает, чего не хватило,
 * и ничего не списывает; уменьшили — заказ принят.
 */
test('заказ: общая нехватка → понятный отказ → уменьшили → принят', async ({ page }) => {
  const api = await apiAs('Manager')
  const product = await createProduct(api)
  await receive(api, product.id, 300)
  const first = await createDish(api, [{ productId: product.id, quantity: 100 }])
  const second = await createDish(api, [{ productId: product.id, quantity: 100 }])

  await signIn(page, 'Manager')
  await page.goto('/orders')
  const menu = page.locator('section[aria-labelledby=orders-menu]')
  await menu.getByRole('searchbox', { name: 'Найти блюдо' }).fill('Тест-блюдо')

  // 2 + 2 порции по 100 г = 400 г при остатке 300 г.
  for (const dish of [first, second]) {
    await menu.getByRole('button', { name: `Добавить «${dish.name}» в заказ` }).click()
    await menu.getByRole('button', { name: `${dish.name}: на порцию больше` }).click()
  }
  const cart = page.locator('section[aria-labelledby=orders-cart]')
  await expect(cart.getByRole('alert')).toContainText('На весь заказ не хватит')
  await expect(cart.getByRole('alert')).toContainText(`${product.name}: нужно 400 г, есть 300 г`)

  await cart.getByRole('button', { name: 'Принять заказ' }).click()
  await expect(cart.getByRole('alert')).toContainText('Заказ не принят: не хватает продуктов')

  await cart.getByRole('button', { name: `${second.name}: на порцию меньше` }).click()
  await expect(cart.getByRole('alert')).toHaveCount(0)
  await cart.getByRole('button', { name: 'Принять заказ' }).click()
  const toast = page.getByRole('status').getByText(/Заказ ORD-\d+ принят/)
  await expect(toast).toBeVisible()
  const number = (await toast.innerText()).match(/ORD-\d+/)![0]

  // Ведём заказ до выдачи: Принят → Готовится → Готов → Выдан.
  const tracking = page.locator('section[aria-labelledby=orders-recent]')
  const order = tracking.getByRole('listitem').filter({ hasText: number })
  await order.getByRole('button', { name: 'Начать готовить', exact: true }).click()
  await order.getByRole('button', { name: 'Готово', exact: true }).click()
  await order.getByRole('button', { name: 'Выдать', exact: true }).click()
  await expect(tracking.getByRole('listitem').filter({ hasText: number })).toHaveCount(0)
  await tracking.getByRole('button', { name: 'Все', exact: true }).click()
  await expect(tracking.getByRole('listitem').filter({ hasText: number })).toContainText('Выдан')

  // Списано ровно 300 г — продукт закончился, оба блюда в стопе.
  await page.goto('/stoplist')
  const now = page.locator('section[aria-labelledby=stop-now]')
  await expect(now.getByText(first.name)).toBeVisible()
  await expect(now.getByText(second.name)).toBeVisible()
})

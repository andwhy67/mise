import { expect, test } from '@playwright/test'
import { apiAs, createDish, createProduct, receive, signIn } from './helpers'

/**
 * Замена ингредиента при нехватке — только с согласия повара:
 * 1) блюдо в стопе, но у продукта есть замена → «С изменениями» в меню, замена видна в строке корзины;
 * 2) общая нехватка → отказ предлагает замену кнопкой → после согласия заказ принят, в деталях видна замена.
 */
test('замена ингредиента: блюдо из стопа с заменой и замена по отказу', async ({ page }) => {
  const api = await apiAs('Manager')
  const spinach = await createProduct(api)
  const arugula = await createProduct(api)
  await receive(api, arugula.id, 1000)
  const card = await (await api.get(`/api/v1/products/${spinach.id}`)).json()
  const put = await api.put(`/api/v1/products/${spinach.id}`, {
    data: { ...card, substitutes: [{ substituteId: arugula.id, ratio: 1 }] },
  })
  expect(put.ok()).toBeTruthy()
  const dish = await createDish(api, [{ productId: spinach.id, quantity: 100 }])
  const second = await createDish(api, [{ productId: spinach.id, quantity: 100 }])

  await signIn(page, 'Chef')
  await page.goto('/orders')
  const menu = page.locator('section[aria-labelledby=orders-menu]')
  await menu.getByRole('searchbox', { name: 'Найти блюдо' }).fill(dish.name)

  // 1. Шпината нет — блюдо в стопе, но сервер предлагает подать его с рукколой («С изменениями»).
  await expect(menu).toContainText(`Можно: ${spinach.name} → ${arugula.name}`)
  await menu.getByRole('button', { name: `Добавить ${dish.name}: ${spinach.name} → ${arugula.name}` }).click()
  const cart = page.locator('section[aria-labelledby=orders-cart]')
  await expect(cart.getByRole('listitem').filter({ hasText: dish.name })).toContainText(`${spinach.name} → ${arugula.name}`)
  await cart.getByRole('button', { name: 'Принять заказ' }).click()
  // Тоста нет: принятый заказ подсвечен в «Заказах в работе», диктору — объявление role=status.
  const placed = page.locator('section[aria-labelledby=orders-recent]').getByRole('status')
  await expect(placed).toHaveText(/Заказ ORD-\d+ принят/)
  const number = (await placed.textContent())!.match(/ORD-\d+/)![0]

  const tracking = page.locator('section[aria-labelledby=orders-recent]')
  const order = tracking.getByRole('listitem').filter({ hasText: number })
  await order.locator('button[aria-expanded]').first().click()
  await expect(order).toContainText(`${spinach.name} → ${arugula.name}`)

  // Руккола списана вместо шпината: 1000 − 100.
  const stock = await (await api.get('/api/v1/stock')).json()
  expect(stock.find((s: { productId: string }) => s.productId === arugula.id).usableQty).toBe(900)

  // 2. Шпината 150 г: каждому блюду хватает, двум вместе — нет. Отказ предлагает рукколу, повар соглашается кнопкой.
  await receive(api, spinach.id, 150)
  await page.reload()
  for (const name of [dish.name, second.name]) {
    await menu.getByRole('searchbox', { name: 'Найти блюдо' }).fill(name)
    await menu.getByRole('button', { name: `Добавить «${name}» в заказ` }).click()
  }
  await cart.getByRole('button', { name: 'Принять заказ' }).click()
  await expect(cart.getByRole('alert')).toContainText('Заказ не принят: не хватает продуктов')
  await cart.getByRole('button', { name: new RegExp(`Заменить на «${arugula.name}»`) }).click()
  await expect(cart.getByRole('list', { name: 'Замены в заказе' })).toContainText(`${spinach.name} → ${arugula.name}`)
  await cart.getByRole('button', { name: 'Принять заказ' }).click()
  // После перезагрузки объявление пустое — текст появится только у нового, второго заказа.
  await expect(placed).toHaveText(/Заказ ORD-\d+ принят/)
  expect(await placed.textContent()).not.toContain(number)
})

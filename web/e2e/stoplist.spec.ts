import { expect, test } from '@playwright/test'
import { apiAs, createDish, createProduct, receive, signIn } from './helpers'

/** Блюдо в стопе «вручную» (CLAUDE.md, правило 6): убрали — в стопе с причиной и в истории; вернули — снова доступно. */
test('стоп-лист: убрать блюдо вручную и вернуть', async ({ page }) => {
  const api = await apiAs('Manager')
  const product = await createProduct(api)
  await receive(api, product.id, 1000)
  const dish = await createDish(api, [{ productId: product.id, quantity: 100 }])

  await signIn(page, 'Manager')
  await page.goto('/stoplist')
  await page.getByRole('button', { name: 'Убрать блюдо из меню' }).click()
  const sheet = page.getByRole('dialog', { name: 'Убрать блюдо из меню' })
  await sheet.getByLabel(dish.name).check()
  await sheet.getByRole('button', { name: 'Убрать из меню' }).click()

  const now = page.locator('section[aria-labelledby=stop-now]')
  const item = now.getByRole('listitem').filter({ hasText: dish.name })
  await expect(item).toContainText('Убрано вручную')
  await item.getByRole('button', { name: 'Вернуть в меню' }).click()
  await expect(now.getByText(dish.name)).toHaveCount(0)
  await expect(page.locator('section[aria-labelledby=stop-history]').getByText(dish.name).first()).toBeVisible()
})

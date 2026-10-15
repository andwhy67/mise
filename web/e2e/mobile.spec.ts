import { expect, test } from '@playwright/test'
import { expectNoHorizontalScroll, signIn } from './helpers'

/** Телефон 390 (DESIGN.md §3): нижняя навигация, никакой прокрутки вбок, корзина — полосой над навигацией. */
test.describe('телефон', () => {
  test.skip(({ viewport }) => (viewport?.width ?? 0) > 500, 'только для проекта phone')

  test('все разделы менеджера без прокрутки вбок', async ({ page }) => {
    await signIn(page, 'Manager')
    for (const path of ['/', '/stock', '/supplies', '/orders', '/kitchen', '/stoplist', '/hot-offers', '/reports', '/alerts', '/catalog', '/simulator', '/users', '/audit', '/more']) {
      await page.goto(path)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      await expectNoHorizontalScroll(page)
    }
  })

  test('нижняя навигация и корзина-полоса', async ({ page }) => {
    await signIn(page, 'Chef')
    const tabs = page.getByRole('navigation', { name: 'Основная навигация' }).last()
    await tabs.getByRole('link', { name: 'Заказ' }).click()
    await expect(page).toHaveURL(/\/orders/)

    await page.getByRole('button', { name: /^Добавить «.+» в заказ$/ }).first().click()
    const bar = page.getByRole('button', { name: 'Оформить' })
    await expect(bar).toBeVisible()
    await bar.click()
    await expect(page.getByRole('dialog', { name: 'Заказ' })).toBeVisible()
    await expectNoHorizontalScroll(page)
  })
})

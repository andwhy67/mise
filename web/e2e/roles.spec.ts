import { expect, test } from '@playwright/test'
import { signIn } from './helpers'

/** Каждая роль видит свои разделы; чужой раздел по прямой ссылке — понятный экран, а не ошибка. */
test.describe('роли', () => {
  const cases = [
    { role: 'Manager', sees: ['Склад', 'Поставки', 'Заказы', 'Отчёты', 'Пользователи', 'Журнал'], hidden: [] },
    { role: 'Chef', sees: ['Заказы', 'Стоп-лист', 'Что продавать'], hidden: ['Поставки', 'Отчёты', 'Пользователи'] },
    { role: 'Warehouse', sees: ['Склад', 'Поставки', 'Стоп-лист'], hidden: ['Заказы', 'Что продавать', 'Отчёты'] },
  ] as const

  for (const { role, sees, hidden } of cases)
    test(`${role}: навигация по роли`, async ({ page }) => {
      await signIn(page, role)
      const nav = page.getByRole('navigation', { name: 'Основная навигация' }).first()
      // У «Стоп-листа» и «Уведомлений» в имени ссылки ещё и счётчик — сравниваем по началу.
      const link = (item: string) => nav.getByRole('link', { name: new RegExp(`^${item}`) })
      for (const item of sees) await expect(link(item)).toBeVisible()
      for (const item of hidden) await expect(link(item)).toHaveCount(0)
      await page.getByRole('button', { name: 'Выйти' }).first().click()
      await expect(page).toHaveURL(/\/login/)
    })

  test('повар открывает «Пользователей» по ссылке — экран «не для вашей роли»', async ({ page }) => {
    await signIn(page, 'Chef')
    await page.goto('/users')
    await expect(page.getByText('Этот раздел не для вашей роли')).toBeVisible()
    await page.getByRole('button', { name: 'Выйти' }).first().click()
  })
})

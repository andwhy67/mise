import { expect, test } from '@playwright/test'
import { unique } from './helpers'

/**
 * Как люди попадают в систему: менеджер создаёт свой ресторан (отдельная организация, пустой справочник),
 * приглашает повара; повар открывает ссылку, регистрируется и оказывается в том же ресторане — и только в нём.
 */
test('новый ресторан → приглашение → повар присоединяется по ссылке', async ({ page, browser }) => {
  const suffix = unique('x').split(' ')[1]!.toLowerCase()
  const restaurant = `Бистро ${suffix}`

  await page.goto('/register')
  await page.getByLabel('Название ресторана').fill(restaurant)
  await page.getByLabel('Ваше имя').fill('Анна')
  await page.getByLabel('Логин').fill(`anna-${suffix}`)
  await page.getByLabel('Пароль').fill('Strong-Pass-1')
  await page.getByRole('button', { name: 'Создать ресторан' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Главное на сегодня' })).toBeVisible()

  // Свой ресторан пуст: демо-продуктов здесь нет.
  await page.goto('/catalog')
  await expect(page.getByText('Шпинат')).toHaveCount(0)

  await page.goto('/users')
  const invites = page.locator('section[aria-labelledby=users-invites]')
  await invites.getByRole('button', { name: 'Пригласить' }).click()
  const code = (await invites.getByRole('listitem').first().innerText()).match(/[A-Z2-9]{4}-[A-Z2-9]{4}/)![0]

  const cook = await browser.newPage()
  await cook.goto(`/join/${code}`)
  await expect(cook.getByText(`Вы войдёте в «${restaurant}» — роль: повар.`)).toBeVisible()
  await cook.getByLabel('Ваше имя').fill('Пётр')
  await cook.getByLabel('Логин').fill(`petr-${suffix}`)
  await cook.getByLabel('Пароль').fill('Strong-Pass-1')
  await cook.getByRole('button', { name: 'Присоединиться' }).click()
  await expect(cook.getByRole('heading', { level: 1, name: 'Главное на сегодня' })).toBeVisible()
  await cook.goto('/more')
  // Название ресторана — в шапке (на десктопе ещё и в сайдбаре и на «Ещё»).
  await expect(cook.getByText(restaurant).first()).toBeVisible()
  await cook.close()

  // Менеджер видит, кто пришёл по коду (повторный вход по тому же коду проверяют интеграционные тесты).
  await page.reload()
  await expect(invites.getByRole('listitem').first()).toContainText('Пётр присоединился')
})

import { expect, test } from '@playwright/test'
import { apiAs, createProduct, signIn } from './helpers'

/** Приём поставки через форму: предпросмотр срока, приём, продукт на складе со своим сроком. */
test('поставка: форма → срок виден заранее → продукт на складе', async ({ page }) => {
  const api = await apiAs('Manager')
  const product = await createProduct(api)

  await signIn(page, 'Warehouse')
  // Как ярлык «Принять поставку» на иконке приложения.
  await page.goto('/supplies?receive=1')
  const sheet = page.getByRole('dialog', { name: 'Принять поставку' })
  await expect(sheet).toBeVisible()

  // Продукт — через поиск по буквам (combobox), как человек: набрать начало названия и выбрать из списка.
  await sheet.getByRole('combobox', { name: 'Продукт' }).fill(product.name.slice(0, 12))
  await sheet.getByRole('option', { name: product.name }).click()
  await sheet.getByLabel('Сколько', { exact: true }).fill('1,5')
  await expect(sheet.getByLabel('Единица', { exact: true })).toHaveValue('Kilogram')
  await sheet.getByLabel('Срок годности', { exact: true }).selectOption('days')
  await sheet.getByLabel('Сколько дней', { exact: true }).fill('2')
  await expect(sheet.getByText(/Испортится: .+ · по сроку хранения/)).toBeVisible()

  await sheet.getByRole('button', { name: 'Принять поставку' }).click()
  await expect(page.getByRole('status').getByText(/Поставка SUP-[\d-]+ принята: 1 продукт/)).toBeVisible()

  await page.goto('/stock')
  await page.getByRole('searchbox', { name: 'Найти продукт' }).fill(product.name)
  const row = page.getByRole('button', { name: new RegExp(product.name) })
  await expect(row).toContainText('1,5 кг')
  await row.click()
  await expect(page.getByText('уйдёт в работу первой')).toBeVisible()
  // Две подписанные шкалы у поставки: остаток и срок.
  await expect(page.getByText('Остаток', { exact: true })).toBeVisible()
  await expect(page.getByText('1,5 кг из 1,5 кг')).toBeVisible()
})

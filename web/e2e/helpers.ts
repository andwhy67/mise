import { expect, request, type APIRequestContext, type Page } from '@playwright/test'

export type Role = 'Manager' | 'Chef' | 'Warehouse'

const baseURL = () => process.env.E2E_BASE_URL ?? 'http://localhost:8081'
export const unique = (prefix: string) => `${prefix} ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`

const contexts = new Map<Role, Promise<APIRequestContext>>()

/**
 * API под ролью (демо-вход) — чтобы подготовить данные для сценария, не завися от демо-склада.
 * Один вход на роль за прогон: токен живёт 15 минут, а лимит входов с одного IP — защита от перебора.
 */
export function apiAs(role: Role): Promise<APIRequestContext> {
  let context = contexts.get(role)
  if (!context) {
    context = login(role)
    contexts.set(role, context)
  }
  return context
}

async function login(role: Role): Promise<APIRequestContext> {
  const anonymous = await request.newContext({ baseURL: baseURL() })
  const response = await anonymous.post('/api/v1/auth/demo', { data: { role } })
  expect(response.ok(), 'демо-вход должен быть включён (Demo__Enabled=true)').toBeTruthy()
  const { accessToken } = (await response.json()) as { accessToken: string }
  await anonymous.dispose()
  return request.newContext({ baseURL: baseURL(), extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` } })
}

export async function createProduct(api: APIRequestContext, name = unique('Тест-продукт'), unit: 'Gram' | 'Milliliter' | 'Piece' = 'Gram') {
  const response = await api.post('/api/v1/products', {
    data: { name, unit, defaultShelfLifeHours: 72, minStockQty: 0, packSize: 1000, category: 'E2E', storageType: null },
  })
  expect(response.status()).toBe(201)
  return { id: ((await response.json()) as { id: string }).id, name }
}

export async function receive(api: APIRequestContext, productId: string, quantity: number) {
  const response = await api.post('/api/v1/supplies', {
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    data: { number: `E2E-${crypto.randomUUID().slice(0, 8)}`, supplier: 'E2E', items: [{ productId, quantity, unit: 'Gram', shelfLifeHours: 72 }] },
  })
  expect(response.status()).toBe(201)
}

export async function createDish(api: APIRequestContext, ingredients: { productId: string; quantity: number }[], name = unique('Тест-блюдо')) {
  const response = await api.post('/api/v1/dishes', { data: { name, category: 'E2E', price: 100, isActive: true, ingredients } })
  expect(response.status()).toBe(201)
  return { id: ((await response.json()) as { id: string }).id, name }
}

/** Вход через кнопку демо-доступа на экране входа — как это сделает человек. */
export async function signIn(page: Page, role: Role) {
  const label = { Manager: 'Менеджер', Chef: 'Повар', Warehouse: 'Склад' }[role]
  await page.goto('/login')
  // Кнопки демо-доступа появляются после загрузки настроек входа; если сессия уже есть — экран сам уведёт на Главную.
  const demo = page.getByRole('button', { name: label, exact: true })
  const home = page.getByRole('heading', { level: 1, name: 'Главное на сегодня' })
  await expect(demo.or(home)).toBeVisible()
  if (await demo.isVisible()) await demo.click()
  await expect(home).toBeVisible()
}

/** Без горизонтальной прокрутки страницы (DESIGN.md §3). */
export async function expectNoHorizontalScroll(page: Page) {
  const { scroll, client } = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, client: document.documentElement.clientWidth }))
  expect(scroll, 'страница не должна прокручиваться вбок').toBeLessThanOrEqual(client)
}

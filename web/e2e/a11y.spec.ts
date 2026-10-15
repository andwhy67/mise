import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'
import { signIn } from './helpers'

/**
 * Доступность (DESIGN.md §11): axe-core по WCAG 2.1 A/AA на основных экранах в обеих темах.
 * Падение — любое нарушение уровня serious/critical (контраст, подписи, роли, порядок заголовков).
 */
const PAGES = ['/', '/stock', '/orders', '/kitchen', '/stoplist', '/hot-offers', '/supplies', '/reports', '/alerts', '/more']

// Без анимаций: проверяем итоговый вид (во время появления текст полупрозрачный), заодно — режим «уменьшить движение».
test.use({ reducedMotion: 'reduce' })

for (const theme of ['light', 'dark'] as const)
  test(`a11y: основные экраны, тема ${theme}`, async ({ page }) => {
    await page.addInitScript((value) => localStorage.setItem('mise.theme', value), theme)
    await signIn(page, 'Manager')
    const problems: string[] = []
    for (const path of PAGES) {
      await page.goto(path)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      // Дождаться данных: скелетоны загрузки не проверяем.
      await expect(page.locator('[role=status][aria-live=polite] .visually-hidden', { hasText: 'Загружаем' })).toHaveCount(0)
      const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
      for (const violation of result.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical'))
        problems.push(`${path}: ${violation.id} — ${violation.help} (${violation.nodes.length}: ${violation.nodes.slice(0, 3).map((n) => n.target.join(' ')).join(' | ')})`)
    }
    expect(problems, problems.join('\n')).toEqual([])
  })

test('a11y: экран входа', async ({ page }) => {
  await page.goto('/login')
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze()
  const serious = result.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
  expect(serious.map((v) => `${v.id}: ${v.help}`), serious.map((v) => JSON.stringify(v.nodes.slice(0, 2).map((n) => n.target))).join('\n')).toEqual([])
})

import { createSystem, defaultBaseConfig, defineConfig } from '@chakra-ui/react'

/**
 * Chakra — провайдер и токены (preflight, шрифты, семантические цвета). Компоненты интерфейса — свои, на SCSS-модулях
 * и нативных элементах (<dialog>, <select>), поэтому берётся базовая конфигурация без рецептов всех компонентов
 * (defaultBaseConfig): основной чанк легче на ~140 кБ. Понадобится компонент Chakra — подключить его рецепт явно.
 * Семантические токены ссылаются на CSS-переменные из tokens.css — светлая и «Графит» переключаются там.
 */
const cssVar = (name: string) => ({ value: { _light: `var(--${name})`, _dark: `var(--${name})` } })

const config = defineConfig({
  preflight: true,
  globalCss: {
    'html, body': { bg: 'var(--bg)', color: 'var(--ink)' },
  },
  theme: {
    tokens: {
      fonts: {
        heading: { value: "'Cormorant Garamond', 'Times New Roman', serif" },
        body: { value: "'Golos Text', system-ui, -apple-system, 'Segoe UI', sans-serif" },
      },
      radii: {
        l1: { value: '6px' },
        l2: { value: '6px' },
        l3: { value: '8px' },
      },
    },
    semanticTokens: {
      colors: {
        bg: { DEFAULT: cssVar('bg'), panel: cssVar('surface'), subtle: cssVar('bg'), muted: cssVar('hairline') },
        fg: { DEFAULT: cssVar('ink'), muted: cssVar('muted'), subtle: cssVar('ink-2') },
        border: { DEFAULT: cssVar('border'), muted: cssVar('hairline'), emphasized: cssVar('border-input') },
        brand: {
          solid: cssVar('brand'),
          contrast: cssVar('on-brand-text'),
          fg: cssVar('brand-strong'),
          muted: cssVar('brand-tint'),
          subtle: cssVar('brand-tint'),
          emphasized: cssVar('brand-tint'),
          focusRing: cssVar('brand-strong'),
        },
      },
      shadows: {
        // Без теней (DESIGN.md §2.3): всплывающие слои отделяются границей.
        sm: { value: '0 0 0 1px var(--border)' },
        md: { value: '0 0 0 1px var(--border)' },
        lg: { value: '0 0 0 1px var(--border)' },
      },
    },
  },
})

export const system = createSystem(defaultBaseConfig, config)

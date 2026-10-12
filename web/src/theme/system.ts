import { createSystem, defaultConfig, defineConfig } from '@chakra-ui/react'

/**
 * Тема Mise. Акцент — синий «Флагман» (#0088CE = brand.500).
 * Для заливок с белым текстом используем brand.600 (#0073B0): контраст ≥ 4.5:1 (WCAG AA).
 * Нейтральные — тёплый off-white / графит. Статусы сроков: базилик / янтарь / красный.
 */
const config = defineConfig({
  globalCss: {
    'html, body': {
      bg: 'bg',
      color: 'fg',
    },
  },
  theme: {
    tokens: {
      fonts: {
        heading: { value: "'Cormorant Garamond', Georgia, 'Times New Roman', serif" },
        body: { value: "'Inter Variable', Inter, system-ui, -apple-system, 'Segoe UI', sans-serif" },
      },
      colors: {
        brand: {
          50: { value: '#E6F4FB' },
          100: { value: '#C2E3F5' },
          200: { value: '#8FCDEF' },
          300: { value: '#57B3E6' },
          400: { value: '#2A9CDB' },
          500: { value: '#0088CE' },
          600: { value: '#0073B0' },
          700: { value: '#005D8F' },
          800: { value: '#00476D' },
          900: { value: '#00324D' },
          950: { value: '#001F30' },
        },
        basil: {
          50: { value: '#EEF7EE' },
          500: { value: '#3E8E41' },
          600: { value: '#2F7533' },
          300: { value: '#7FC283' },
        },
        amber: {
          50: { value: '#FFF6E5' },
          500: { value: '#E09B10' },
          600: { value: '#B37800' },
          300: { value: '#F5C35A' },
        },
        warm: {
          50: { value: '#FAF8F4' },
          100: { value: '#F2EEE7' },
          200: { value: '#E5DFD5' },
          800: { value: '#22252B' },
          900: { value: '#181A1F' },
          950: { value: '#111317' },
        },
      },
    },
    // Условия — `_light`/`_dark`, а не `base`: defaultConfig задаёт bg/border через `_light`,
    // и `base` не перекрыл бы их при слиянии конфигов.
    semanticTokens: {
      colors: {
        brand: {
          solid: { value: { _light: '{colors.brand.600}', _dark: '{colors.brand.600}' } },
          contrast: { value: '#FFFFFF' },
          fg: { value: { _light: '{colors.brand.700}', _dark: '{colors.brand.300}' } },
          muted: { value: { _light: '{colors.brand.100}', _dark: '{colors.brand.900}' } },
          subtle: { value: { _light: '{colors.brand.50}', _dark: '{colors.brand.950}' } },
          emphasized: { value: { _light: '{colors.brand.200}', _dark: '{colors.brand.800}' } },
          focusRing: { value: { _light: '{colors.brand.500}', _dark: '{colors.brand.400}' } },
        },
        bg: {
          DEFAULT: { value: { _light: '{colors.warm.50}', _dark: '{colors.warm.950}' } },
          panel: { value: { _light: '#FFFFFF', _dark: '{colors.warm.900}' } },
          subtle: { value: { _light: '{colors.warm.100}', _dark: '{colors.warm.800}' } },
        },
        border: {
          DEFAULT: { value: { _light: '{colors.warm.200}', _dark: '{colors.warm.800}' } },
        },
        // Статусы срока годности (всегда дублируются иконкой/текстом в UI).
        shelf: {
          fresh: { value: { _light: '{colors.basil.600}', _dark: '{colors.basil.300}' } },
          warning: { value: { _light: '{colors.amber.600}', _dark: '{colors.amber.300}' } },
          critical: { value: { _light: '{colors.red.600}', _dark: '{colors.red.300}' } },
          expired: { value: { _light: '{colors.gray.600}', _dark: '{colors.gray.400}' } },
        },
      },
    },
  },
})

export const system = createSystem(defaultConfig, config)

import i18n, { type BackendModule, type ReadCallback } from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { initReactI18next } from 'react-i18next'

/**
 * Языки интерфейса (DESIGN.md §9a). Новый язык = новая папка src/i18n/locales/<код> с теми же файлами
 * + строка здесь; код компонентов не меняется.
 */
export const LANGUAGES = [
  { code: 'ru', label: 'RU', name: 'Русский' },
  { code: 'en', label: 'EN', name: 'English' },
] as const

export type LanguageCode = (typeof LANGUAGES)[number]['code']
export const DEFAULT_LANGUAGE: LanguageCode = 'ru'
export const NAMESPACES = ['common', 'format', 'auth', 'dashboard', 'ui', 'stock', 'stoplist', 'offers', 'orders', 'alerts', 'supplies', 'reports', 'simulator', 'catalog', 'users', 'audit'] as const
export const LANGUAGE_STORAGE_KEY = 'mise.language'

/** Каждый раздел каждого языка — отдельный чанк, подгружается при первом использовании. */
const loaders = import.meta.glob<{ default: Record<string, unknown> }>('./locales/*/*.json')

const lazyBackend: BackendModule = {
  type: 'backend',
  init: () => {},
  read(language: string, namespace: string, callback: ReadCallback) {
    const load = loaders[`./locales/${language}/${namespace}.json`]
    if (!load) {
      callback(null, {})
      return
    }
    load().then(
      (module) => callback(null, module.default),
      (error: unknown) => callback(error instanceof Error ? error : new Error(String(error)), null),
    )
  },
}

export function initI18n() {
  if (i18n.isInitialized) return i18n
  void i18n
    .use(lazyBackend)
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      supportedLngs: LANGUAGES.map((l) => l.code),
      fallbackLng: DEFAULT_LANGUAGE,
      load: 'languageOnly',
      ns: [...NAMESPACES],
      defaultNS: 'common',
      interpolation: { escapeValue: false },
      detection: {
        order: ['localStorage', 'navigator'],
        lookupLocalStorage: LANGUAGE_STORAGE_KEY,
        caches: ['localStorage'],
      },
      react: { useSuspense: true },
    })
  i18n.on('languageChanged', (language) => {
    document.documentElement.lang = language
  })
  return i18n
}

export { i18n }

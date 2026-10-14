import i18next, { type i18n as I18n } from 'i18next'
import { NAMESPACES } from '@/i18n'

/** Синхронно загруженные переводы для тестов: отдельный экземпляр i18next на язык. */
const locales = import.meta.glob<{ default: Record<string, unknown> }>('../i18n/locales/*/*.json', { eager: true })

export async function createTestI18n(language: string): Promise<I18n> {
  const resources: Record<string, Record<string, Record<string, unknown>>> = {}
  for (const [path, module] of Object.entries(locales)) {
    const match = /locales\/([^/]+)\/([^/]+)\.json$/.exec(path)
    if (!match) continue
    const [, lng, ns] = match as unknown as [string, string, string]
    resources[lng] ??= {}
    resources[lng][ns] = module.default
  }
  const instance = i18next.createInstance()
  await instance.init({
    lng: language,
    fallbackLng: 'ru',
    ns: [...NAMESPACES],
    defaultNS: 'common',
    resources,
    interpolation: { escapeValue: false },
  })
  return instance
}

import { Monitor, Moon, Sun } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { LANGUAGES } from '@/i18n'
import { originOf, withViewTransition } from '@/lib/viewTransition'
import { useTheme } from '@/theme/colorScheme'
import styles from './Switchers.module.scss'

const THEMES = [
  { value: 'light', icon: Sun },
  { value: 'dark', icon: Moon },
  { value: 'system', icon: Monitor },
] as const

/** «Светлая / Графит / Как в системе» (DESIGN.md §2.1a). Иконки с подписью — не только иконкой. */
export function ThemeSwitcher({ withLabels = false }: { withLabels?: boolean }) {
  const { t } = useTranslation()
  const { theme, setTheme } = useTheme()
  return (
    <div className={styles.group} role="group" aria-label={t('theme.label')}>
      {THEMES.map(({ value, icon: Icon }) => (
        <button
          key={value}
          type="button"
          className={styles.option}
          aria-pressed={theme === value}
          title={t(`theme.${value}`)}
          onClick={(event) => {
            if (theme === value) return
            const origin = originOf(event.currentTarget)
            void withViewTransition('theme', () => setTheme(value), origin)
          }}
        >
          <Icon aria-hidden="true" strokeWidth={1.8} />
          <span className={withLabels ? undefined : 'visually-hidden'}>{t(`theme.${value}`)}</span>
        </button>
      ))}
    </div>
  )
}

/** «RU · EN» — список языков берётся из i18n, новый язык появится сам. */
export function LanguageSwitcher({ inverse = false }: { inverse?: boolean }) {
  const { t, i18n } = useTranslation()
  const current = i18n.resolvedLanguage ?? i18n.language
  return (
    <div className={`${styles.languages} ${inverse ? styles.inverse : ''}`} role="group" aria-label={t('language.label')}>
      {LANGUAGES.map((language, index) => (
        <span key={language.code} className={styles.languageItem}>
          {index > 0 && <span aria-hidden="true">·</span>}
          <button
            type="button"
            lang={language.code}
            className={styles.language}
            aria-pressed={current === language.code}
            aria-label={language.name}
            onClick={() => {
              if (current === language.code) return
              void withViewTransition('language', () => i18n.changeLanguage(language.code))
            }}
          >
            {language.label}
          </button>
        </span>
      ))}
    </div>
  )
}

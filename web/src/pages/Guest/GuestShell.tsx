import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { LanguageSwitcher, ThemeSwitcher } from '@/components/Switchers'
import styles from './GuestShell.module.scss'

/**
 * Каркас страниц гостя: без навигации сотрудников и без «живого» соединения. Название ресторана — главное,
 * переключатели языка и темы — справа (гость может быть иностранцем или сидеть в тёмном зале).
 */
export function GuestShell({ restaurant, table, action, children }: { restaurant?: string; table?: number | null; action?: ReactNode; children: ReactNode }) {
  const { t } = useTranslation('guest')
  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <div className={styles.heading}>
          <p className={styles.kicker}>
            {t('menu')}
            {table ? ` · ${t('table', { table })}` : ''}
          </p>
          {restaurant && <h1 className={styles.title}>{restaurant}</h1>}
        </div>
        <div className={styles.tools}>
          {action}
          <LanguageSwitcher />
          <ThemeSwitcher />
        </div>
      </header>
      <main className={styles.main}>{children}</main>
    </div>
  )
}

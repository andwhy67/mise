import type { ReactNode } from 'react'
import styles from './Page.module.scss'

interface PageProps {
  title: string
  /** Строка под заголовком: пояснение или «Среда, 14 октября · сейчас 14:07». */
  subtitle?: ReactNode
  /** Действия справа от заголовка (на телефоне — под ним). */
  actions?: ReactNode
  children: ReactNode
}

/** Каркас экрана: заголовок H1 (Cormorant), пояснение, действия, затем содержимое с ровным ритмом. */
export function Page({ title, subtitle, actions, children }: PageProps) {
  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <div className={styles.heading}>
          <h1 className={styles.title}>{title}</h1>
          {subtitle && <p className={styles.subtitle}>{subtitle}</p>}
        </div>
        {actions && <div className={styles.actions}>{actions}</div>}
      </header>
      {children}
    </div>
  )
}

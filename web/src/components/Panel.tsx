import type { ReactNode } from 'react'
import styles from './Panel.module.scss'

interface PanelProps {
  /** Заголовок-вопрос или действие (DESIGN.md §1.1, п. 8). */
  title?: ReactNode
  /** Строка пояснения под заголовком. */
  subtitle?: ReactNode
  /** Действие справа от заголовка (ссылка «Показать весь склад»). */
  action?: ReactNode
  children: ReactNode
  className?: string
  /** id заголовка — для aria-labelledby секции. */
  titleId?: string
}

/** Панель: белая поверхность, граница 1px, радиус 8, без тени (DESIGN.md §5). */
export function Panel({ title, subtitle, action, children, className, titleId }: PanelProps) {
  return (
    <section className={`${styles.panel} ${className ?? ''}`} aria-labelledby={title ? titleId : undefined}>
      {(title || action) && (
        <header className={styles.header}>
          <div>
            {title && (
              <h2 className={styles.title} id={titleId}>
                {title}
              </h2>
            )}
            {subtitle && <p className={styles.subtitle}>{subtitle}</p>}
          </div>
          {action && <div className={styles.action}>{action}</div>}
        </header>
      )}
      {children}
    </section>
  )
}

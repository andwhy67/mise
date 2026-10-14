import type { ReactNode } from 'react'
import styles from './ScrollArea.module.scss'

/**
 * Длинный список прокручивается внутри своей панели, а не всей страницей (на широком экране).
 * Фокусируемая область с подписью — листается с клавиатуры (axe: scrollable-region-focusable). На телефоне — обычная страница.
 */
export function ScrollArea({ label, children, size = 'page' }: { label: string; children: ReactNode; size?: 'page' | 'half' }) {
  return (
    <div className={`${styles.area} ${styles[size]}`} tabIndex={0} role="region" aria-label={label}>
      {children}
    </div>
  )
}

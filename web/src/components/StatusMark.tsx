import type { ReactNode } from 'react'
import type { Tone } from '@/lib/format'
import styles from './StatusMark.module.scss'

interface StatusMarkProps {
  tone: Tone
  children: ReactNode
  /** Жирность фразы: на главной — полужирная, в подписях — обычная. */
  weight?: 'regular' | 'strong'
}

/** Единственный способ показать статус (DESIGN.md §4): квадрат 8×8 + фраза цветом статуса. */
export function StatusMark({ tone, children, weight = 'strong' }: StatusMarkProps) {
  return (
    <span className={`${styles.mark} ${styles[tone]} ${weight === 'strong' ? styles.strong : ''}`}>
      <span className={styles.square} aria-hidden="true" />
      <span>{children}</span>
    </span>
  )
}

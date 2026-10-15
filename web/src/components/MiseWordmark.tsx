import styles from './MiseWordmark.module.scss'

interface MiseWordmarkProps {
  /** На синем фоне экрана входа — белый вариант. */
  inverse?: boolean
  size?: 'md' | 'lg'
  /** Свёрнутый сайдбар: только монограмма, слово остаётся для диктора. */
  compact?: boolean
}

/** Монограмма «M» в квадрате + слово «Mise» (DESIGN.md §5). */
export function MiseWordmark({ inverse = false, size = 'md', compact = false }: MiseWordmarkProps) {
  return (
    <span className={`${styles.wordmark} ${inverse ? styles.inverse : ''} ${styles[size]}`}>
      <span className={styles.monogram} aria-hidden="true">
        M
      </span>
      <span className={compact ? 'visually-hidden' : styles.word}>Mise</span>
    </span>
  )
}

import styles from './MiseWordmark.module.scss'

interface MiseWordmarkProps {
  /** На синем фоне экрана входа — белый вариант. */
  inverse?: boolean
  size?: 'md' | 'lg'
}

/** Монограмма «M» в квадрате + слово «Mise» (DESIGN.md §5). */
export function MiseWordmark({ inverse = false, size = 'md' }: MiseWordmarkProps) {
  return (
    <span className={`${styles.wordmark} ${inverse ? styles.inverse : ''} ${styles[size]}`}>
      <span className={styles.monogram} aria-hidden="true">
        M
      </span>
      <span className={styles.word}>Mise</span>
    </span>
  )
}

import type { CSSProperties } from 'react'
import styles from './PrepTrayBackground.module.scss'

interface Tray {
  name: string
  /** Доля оставшегося срока, 0…1. */
  freshness: number
}

const trays: Tray[] = [
  { name: 'Сливки', freshness: 0.22 },
  { name: 'Томаты', freshness: 0.8 },
  { name: 'Лосось', freshness: 0.55 },
  { name: 'Базилик', freshness: 0.35 },
  { name: 'Курица', freshness: 0.7 },
  { name: 'Пармезан', freshness: 0.95 },
  { name: 'Шпинат', freshness: 0.6 },
  { name: 'Моцарелла', freshness: 0.18 },
  { name: 'Лимоны', freshness: 0.9 },
  { name: 'Креветки', freshness: 0.45 },
  { name: 'Рукола', freshness: 0.65 },
  { name: 'Сыр фета', freshness: 0.85 },
]

const WARNING_THRESHOLD = 0.25

/** Декоративная сетка лотков на раздаче (aria-hidden — чистый декор). */
export function PrepTrayBackground() {
  return (
    <div className={styles.grid} aria-hidden>
      {trays.map((tray, i) => (
        <div
          key={tray.name}
          className={`${styles.tray} ${tray.freshness < WARNING_THRESHOLD ? styles.warning : ''}`}
          style={{ '--i': i, '--fresh': `${Math.round(tray.freshness * 100)}%` } as CSSProperties}
        >
          <span className={styles.name}>{tray.name}</span>
          <div className={styles.bar}>
            <span />
          </div>
        </div>
      ))}
    </div>
  )
}

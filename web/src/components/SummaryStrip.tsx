import { ChevronRight } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import type { Tone } from '@/lib/format'
import styles from './SummaryStrip.module.scss'

export interface SummaryItem {
  id: string
  value: ReactNode
  tone: Tone | 'neutral'
  /** Полная фраза: цифра без неё не выводится (DESIGN.md §1.1, п. 2). */
  phrase: string
  linkLabel: string
  to: string
}

/** Сводка из 2–3 ячеек с hairline-разделителями; на телефоне — строки-ссылки с шевроном. */
export function SummaryStrip({ items, label }: { items: SummaryItem[]; label: string }) {
  return (
    <section className={styles.strip} aria-label={label}>
      {items.map((item) => (
        <div key={item.id} className={styles.cell}>
          {/* key по значению: при live-обновлении новое число мягко «встаёт» на место. */}
          <span key={String(item.value)} className={`${styles.value} ${styles[item.tone]}`}>
            {item.value}
          </span>
          <div className={styles.text}>
            <p className={styles.phrase}>{item.phrase}</p>
            <Link to={item.to} className={styles.link}>
              {item.linkLabel}
            </Link>
          </div>
          {/* Мобильная версия: вся строка — ссылка (DESIGN.md §6.4). */}
          <Link to={item.to} className={styles.rowLink} aria-label={`${item.phrase}: ${item.linkLabel}`}>
            <ChevronRight aria-hidden="true" strokeWidth={1.8} />
          </Link>
        </div>
      ))}
    </section>
  )
}

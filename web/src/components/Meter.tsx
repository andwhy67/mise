import type { Tone } from '@/lib/format'
import styles from './Meter.module.scss'

interface MeterProps {
  /** Что показывает полоска: «Остаток», «Срок», «Заказ берёт». */
  label: string
  /** Значение словами и цифрами — полоска его не заменяет: «420 г из 600 г», «Завтра в 03:30». */
  value: string
  /** Заполненность 0…1 (больше 1 — переполнение, рисуется полной). */
  fraction: number
  tone: Tone
}

/** Подписанная полоска (DESIGN.md §5, ShelfLifeBar): подпись слева, значение справа, тонкая шкала под ними. */
export function Meter({ label, value, fraction, tone }: MeterProps) {
  const percent = Math.max(0, Math.min(1, fraction)) * 100
  return (
    <div className={styles.meter}>
      <div className={styles.head}>
        <span className={styles.label}>{label}</span>
        <span className={`${styles.value} ${styles[tone]}`}>{value}</span>
      </div>
      <div className={styles.track} aria-hidden="true">
        <div className={`${styles.fill} ${styles[`bar-${tone}`]}`} style={{ width: `${percent}%` }} />
      </div>
    </div>
  )
}

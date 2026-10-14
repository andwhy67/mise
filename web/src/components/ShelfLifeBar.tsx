import type { When } from '@/lib/format'
import { StatusMark } from './StatusMark'
import styles from './ShelfLifeBar.module.scss'

/** Полоска срока — только на детальных экранах и всегда с подписью о времени (DESIGN.md §5). */
export function ShelfLifeBar({ remainingFraction, when }: { remainingFraction: number; when: When }) {
  const percent = Math.max(0, Math.min(1, remainingFraction)) * 100
  return (
    <div className={styles.wrap}>
      <div className={styles.track} aria-hidden="true">
        <div className={`${styles.fill} ${styles[when.tone]}`} style={{ width: `${percent}%` }} />
      </div>
      <StatusMark tone={when.tone} weight="regular">
        {when.text}
      </StatusMark>
    </div>
  )
}

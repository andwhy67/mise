import styles from './StopListRow.module.scss'

interface StopListRowProps {
  /** Группа: «Закончилось» (crit) или «Скоро закончится» (warn). */
  group: 'out' | 'soon'
  groupLabel: string
  dishName: string
  /** Причина обычным языком: «Закончился маскарпоне: осталось 80 г, а на порцию нужно 120 г». */
  reason: string
}

/** Пункт «Нельзя заказать» (DESIGN.md §5): мелкий заголовок группы цветом, название, причина. */
export function StopListRow({ group, groupLabel, dishName, reason }: StopListRowProps) {
  return (
    <article className={styles.row}>
      <p className={`${styles.group} ${group === 'out' ? styles.out : styles.soon}`}>{groupLabel}</p>
      <h3 className={styles.dish}>{dishName}</h3>
      <p className={styles.reason}>{reason}</p>
    </article>
  )
}

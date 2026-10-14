import type { When } from '@/lib/format'
import { StatusMark } from './StatusMark'
import styles from './ExpiryList.module.scss'

export interface ExpiryItem {
  id: string
  productName: string
  /** Сколько осталось, уже словами: «2,4 кг». */
  quantity: string
  when: When
}

/** «Что скоро испортится»: продукт — сколько — когда (DESIGN.md §5). Сортирует вызывающий: раньше — выше. */
export function ExpiryList({ items, label }: { items: ExpiryItem[]; label: string }) {
  return (
    <ul className={styles.list} aria-label={label}>
      {items.map((item) => (
        <li key={item.id} className={styles.row}>
          <span className={styles.product}>{item.productName}</span>
          <span className={styles.quantity}>{item.quantity}</span>
          <span className={styles.when}>
            <StatusMark tone={item.when.tone}>{item.when.text}</StatusMark>
          </span>
        </li>
      ))}
    </ul>
  )
}

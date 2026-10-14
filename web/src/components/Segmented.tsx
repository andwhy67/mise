import styles from './Segmented.module.scss'

export interface SegmentedOption<T extends string> {
  value: T
  label: string
  /** Сколько элементов в разделе — число текстом, без плашки (DESIGN.md §1, п. 2). */
  count?: number
}

interface SegmentedProps<T extends string> {
  label: string
  value: T
  options: SegmentedOption<T>[]
  onChange: (value: T) => void
}

/** Фильтр-переключатель: текстовые кнопки с подчёркиванием выбранной, без чипов и плашек. */
export function Segmented<T extends string>({ label, value, options, onChange }: SegmentedProps<T>) {
  return (
    <div className={styles.group} role="group" aria-label={label}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          className={styles.option}
          aria-pressed={option.value === value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
          {option.count !== undefined && <span className={styles.count}>{option.count}</span>}
        </button>
      ))}
    </div>
  )
}

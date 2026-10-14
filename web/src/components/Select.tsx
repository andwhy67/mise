import { ChevronDown } from 'lucide-react'
import { useId, type SelectHTMLAttributes } from 'react'
import styles from './Select.module.scss'

export interface SelectOption {
  value: string
  label: string
}

export interface SelectGroup {
  label: string
  options: SelectOption[]
}

interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'id'> {
  label: string
  /** Скрыть метку визуально (в табличной строке), оставив её для диктора. */
  hideLabel?: boolean
  options?: SelectOption[]
  /** Группы — например, продукты по категориям. */
  groups?: SelectGroup[]
  placeholder?: string
  error?: string
}

/** Выпадающий список на нативном <select>: на телефоне — системный выбор, доступен с клавиатуры и диктору. */
export function Select({ label, hideLabel, options, groups, placeholder, error, className, ...select }: SelectProps) {
  const id = useId()
  return (
    <div className={`${styles.field} ${className ?? ''}`}>
      <label htmlFor={id} className={hideLabel ? 'visually-hidden' : styles.label}>
        {label}
      </label>
      <div className={styles.control}>
        <select
          id={id}
          className={styles.select}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? `${id}-error` : undefined}
          {...select}
        >
          {placeholder !== undefined && (
            <option value="" disabled>
              {placeholder}
            </option>
          )}
          {options?.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
          {groups?.map((group) => (
            <optgroup key={group.label} label={group.label}>
              {group.options.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </optgroup>
          ))}
        </select>
        <ChevronDown className={styles.chevron} aria-hidden="true" strokeWidth={1.8} />
      </div>
      {error && (
        <p id={`${id}-error`} className={styles.error}>
          {error}
        </p>
      )}
    </div>
  )
}

import { useId, type InputHTMLAttributes } from 'react'
import styles from './Field.module.scss'

interface FieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string
  /** Скрыть метку визуально (повторяющиеся строки формы), оставив её для диктора. */
  hideLabel?: boolean
  hint?: string
  error?: string
}

/** Поле ввода (DESIGN.md §5): метка сверху, высота 48, текст 16px (iOS не масштабирует), ошибка рядом с полем. */
export function Field({ label, hideLabel, hint, error, className, ...input }: FieldProps) {
  const id = useId()
  const describedBy = [hint ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined
  return (
    <div className={`${styles.field} ${className ?? ''}`}>
      <label htmlFor={id} className={hideLabel ? 'visually-hidden' : styles.label}>
        {label}
      </label>
      <input id={id} className={styles.input} aria-invalid={error ? true : undefined} aria-describedby={describedBy} {...input} />
      {hint && !error && (
        <p id={`${id}-hint`} className={styles.hint}>
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className={styles.error}>
          {error}
        </p>
      )}
    </div>
  )
}

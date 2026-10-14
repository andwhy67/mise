import { Minus, Plus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import styles from './Stepper.module.scss'

interface StepperProps {
  value: number
  onChange: (value: number) => void
  /** Для чего число — попадает в подписи кнопок: «Блины: убрать порцию». */
  label: string
  min?: number
  max?: number
}

/** Количество порций: − N +. Кнопки 44×44, значение объявляется диктором при изменении. */
export function Stepper({ value, onChange, label, min = 0, max = 99 }: StepperProps) {
  const { t } = useTranslation()
  return (
    <div className={styles.stepper} role="group" aria-label={label}>
      <button
        type="button"
        className={styles.button}
        aria-label={t('stepper.less', { name: label })}
        disabled={value <= min}
        onClick={() => onChange(Math.max(min, value - 1))}
      >
        <Minus aria-hidden="true" strokeWidth={1.8} />
      </button>
      <span key={value} className={styles.value} aria-live="polite">
        {value}
      </span>
      <button
        type="button"
        className={styles.button}
        aria-label={t('stepper.more', { name: label })}
        disabled={value >= max}
        onClick={() => onChange(Math.min(max, value + 1))}
      >
        <Plus aria-hidden="true" strokeWidth={1.8} />
      </button>
    </div>
  )
}

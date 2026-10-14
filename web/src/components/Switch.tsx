import styles from './Switch.module.scss'

interface SwitchProps {
  id: string
  checked: boolean
  /** Подпись для диктора (видимая подпись — рядом, в строке настройки). */
  label: string
  disabled?: boolean
  onChange: (next: boolean) => void
}

/** Переключатель «вкл/выкл»: настоящий checkbox с role=switch — работает с клавиатуры и диктором. */
export function Switch({ id, checked, label, disabled, onChange }: SwitchProps) {
  return (
    <label className={styles.switch} htmlFor={id}>
      <input id={id} type="checkbox" role="switch" checked={checked} disabled={disabled} aria-label={label} onChange={(e) => onChange(e.target.checked)} />
      <span className={styles.track} aria-hidden="true">
        <span className={styles.thumb} />
      </span>
    </label>
  )
}

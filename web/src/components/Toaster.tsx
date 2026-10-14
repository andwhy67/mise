import { X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { toast, useToasts } from '@/lib/toast'
import styles from './Toaster.module.scss'

/** Область сообщений: внизу справа (на телефоне — над нижней навигацией), читается экранным диктором. */
export function Toaster() {
  const { t } = useTranslation()
  const items = useToasts()
  return (
    <div className={styles.region} role="status" aria-live="polite">
      {items.map((item) => (
        <div key={item.id} className={styles.toast}>
          <span className={`${styles.square} ${styles[item.tone]}`} aria-hidden="true" />
          <p className={styles.text}>{item.text}</p>
          {item.action && (
            <button
              type="button"
              className={styles.action}
              onClick={() => {
                item.action!.onClick()
                toast.dismiss(item.id)
              }}
            >
              {item.action.label}
            </button>
          )}
          <button type="button" className={styles.close} aria-label={t('actions.close')} onClick={() => toast.dismiss(item.id)}>
            <X aria-hidden="true" strokeWidth={1.8} />
          </button>
        </div>
      ))}
    </div>
  )
}

import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/api/errors'
import { useErrorText } from '@/lib/useErrorText'
import { Button } from './Button'
import styles from './StateBlocks.module.scss'

/** Пустое состояние (DESIGN.md §5): засечный заголовок + подсказка о следующем действии, без иллюстраций. */
export function EmptyState({ title, children, action }: { title?: string; children?: ReactNode; action?: ReactNode }) {
  const { t } = useTranslation()
  return (
    <div className={styles.empty}>
      <p className={styles.emptyTitle}>{title ?? t('state.empty')}</p>
      {children && <p className={styles.hint}>{children}</p>}
      {action}
    </div>
  )
}

/** Загрузка: резервирует место, чтобы содержимое не прыгало (CLS). */
export function LoadingState({ lines = 3 }: { lines?: number }) {
  const { t } = useTranslation()
  return (
    <div className={styles.loading} role="status" aria-live="polite">
      <span className="visually-hidden">{t('state.loading')}</span>
      {Array.from({ length: lines }, (_, i) => (
        <span key={i} className={styles.skeleton} aria-hidden="true" />
      ))}
    </div>
  )
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const { t } = useTranslation()
  const errorText = useErrorText()
  const network = error instanceof ApiError && error.status === 0
  return (
    <div className={styles.error} role="alert">
      <p className={styles.errorTitle}>
        <span className={styles.square} aria-hidden="true" />
        {network ? errorText(error) : t('state.error')}
      </p>
      <p className={styles.hint}>{t('state.errorHint')}</p>
      {onRetry && (
        <Button variant="text" onClick={onRetry}>
          {t('actions.retry')}
        </Button>
      )}
    </div>
  )
}

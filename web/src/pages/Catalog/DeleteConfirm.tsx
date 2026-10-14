import { Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/Button'
import styles from './CatalogForms.module.scss'

/** Удаление в два шага: «Удалить» → «Точно удалить?» — без отдельного окна поверх листа. */
export function DeleteConfirm({ onConfirm, pending }: { onConfirm: () => void; pending: boolean }) {
  const { t } = useTranslation('catalog')
  const [asked, setAsked] = useState(false)
  if (!asked)
    return (
      <Button variant="text" icon={<Trash2 />} className={styles.danger} onClick={() => setAsked(true)}>
        {t('delete')}
      </Button>
    )
  return (
    <span className={styles.confirm} role="group" aria-label={t('deleteConfirm')}>
      <span className={styles.confirmText}>{t('deleteConfirm')}</span>
      <Button variant="text" className={styles.danger} disabled={pending} onClick={onConfirm}>
        {t('deleteYes')}
      </Button>
      <Button variant="text" onClick={() => setAsked(false)}>
        {t('deleteNo')}
      </Button>
    </span>
  )
}

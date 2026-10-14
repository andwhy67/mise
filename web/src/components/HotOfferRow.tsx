import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { VERDICT_TONE, type OfferVerdict } from '@/lib/format'
import { StatusMark } from './StatusMark'
import styles from './HotOfferRow.module.scss'

interface HotOfferRowProps {
  rank: number
  dishName: string
  verdict: OfferVerdict
  verdictText: string
  /** Причина обычным языком, 1–2 предложения. */
  reason: string
  /** Подробный расчёт — только по ссылке «Как это посчитано». */
  calculation?: ReactNode
  /** На узком экране — без номера и раскрытия (DESIGN.md §6.4). */
  compact?: boolean
}

/** Строка «Что продавать»: номер, блюдо, вердикт словами, причина; без баллов и процентов (DESIGN.md §5). */
export function HotOfferRow({ rank, dishName, verdict, verdictText, reason, calculation, compact = false }: HotOfferRowProps) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const detailsId = useId()

  return (
    <article className={`${styles.row} ${compact ? styles.compact : ''}`}>
      {!compact && (
        <span className={styles.rank} aria-hidden="true">
          {rank}
        </span>
      )}
      <div className={styles.body}>
        <h3 className={styles.dish}>{dishName}</h3>
        <StatusMark tone={VERDICT_TONE[verdict]}>{verdictText}</StatusMark>
        <p className={styles.reason}>{reason}</p>
        {calculation && !compact && (
          <>
            <button type="button" className={styles.toggle} aria-expanded={open} aria-controls={detailsId} onClick={() => setOpen(!open)}>
              {open ? t('actions.hideCalculation') : t('actions.howCalculated')}
            </button>
            <div id={detailsId} className={styles.details} hidden={!open}>
              {calculation}
            </div>
          </>
        )}
      </div>
    </article>
  )
}

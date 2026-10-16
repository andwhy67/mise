import { CookingPot } from 'lucide-react'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { usePreps, type PrepItem } from '@/api/prep'
import { Button } from '@/components/Button'
import { Panel } from '@/components/Panel'
import { ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { useFormat } from '@/lib/useFormat'
import { PrepSheet } from './PrepSheet'
import styles from './PrepPanel.module.scss'

/** Заготовки (соус, бульон): сколько есть, на сколько хватит продуктов, кнопка «Приготовить». */
export function PrepPanel() {
  const { t } = useTranslation('stock')
  const query = usePreps()
  const [cooking, setCooking] = useState<PrepItem | null>(null)
  const titleId = useId()

  if (query.isSuccess && query.data.length === 0) return null

  return (
    <Panel title={t('prep.title')} subtitle={t('prep.subtitle')} titleId={titleId}>
      {query.isPending ? (
        <LoadingState lines={2} />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <ul className={styles.list} aria-labelledby={titleId}>
          {query.data.map((prep) => (
            <PrepCard key={prep.id} prep={prep} onCook={() => setCooking(prep)} />
          ))}
        </ul>
      )}
      <PrepSheet prep={cooking} onClose={() => setCooking(null)} />
    </Panel>
  )
}

function PrepCard({ prep, onCook }: { prep: PrepItem; onCook: () => void }) {
  const { t } = useTranslation('stock')
  const f = useFormat()
  const canCook = prep.maxQuantity > 0

  return (
    <li className={styles.card}>
      <div className={styles.head}>
        <CookingPot className={styles.icon} aria-hidden="true" strokeWidth={1.8} />
        <span className={styles.name}>{prep.name}</span>
      </div>
      <p className={styles.amount}>{prep.usable > 0 ? f.qty(prep.usable, prep.unit) : t('none')}</p>
      <p className={styles.meta}>
        {canCook ? (
          t('prep.canMake', { qty: f.qty(prep.maxQuantity, prep.unit) })
        ) : (
          <StatusMark tone="crit" weight="regular">
            {t('prep.nothingToMake')}
          </StatusMark>
        )}
      </p>
      <p className={styles.meta}>{t('prep.keeps', { hours: prep.shelfLifeHours })}</p>
      <Button variant="secondary" className={styles.action} disabled={!canCook} onClick={onCook}>
        {t('prep.cook')}
      </Button>
    </li>
  )
}

import { useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useHotOffers } from '@/api/queries'
import type { HotOffer } from '@/api/types'
import { HotOfferRow } from '@/components/HotOfferRow'
import { LiveIndicator } from '@/components/LiveIndicator'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { offerVerdict } from '@/lib/format'
import { offerCalculation, offerReason } from '@/lib/texts'
import { useFlip } from '@/lib/useFlip'
import { useFormat } from '@/lib/useFormat'
import styles from './HotOffersPage.module.scss'

/** Что продавать (DESIGN.md §6.3): полный список с вердиктами и причинами; при live-обновлении строки переезжают. */
export function HotOffersPage() {
  const { t } = useTranslation('offers')
  const f = useFormat()
  const query = useHotOffers()
  const offers = query.data?.offers ?? []

  return (
    <Page
      title={t('title')}
      subtitle={query.data ? t('updated', { time: f.time(query.data.generatedAt) }) : t('subtitle')}
      actions={<LiveIndicator />}
    >
      <div className={styles.columns}>
        <Panel title={t('list.title')} subtitle={t('list.subtitle')} titleId="offers-list">
          {query.isPending ? (
            <LoadingState lines={5} />
          ) : query.isError ? (
            <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          ) : offers.length === 0 ? (
            <EmptyState title={t('list.empty')}>{t('list.emptyHint')}</EmptyState>
          ) : (
            <OfferList offers={offers} version={query.data?.generatedAt} />
          )}
        </Panel>
        <HowItWorks />
      </div>
    </Page>
  )
}

function OfferList({ offers, version }: { offers: HotOffer[]; version: unknown }) {
  const { t } = useTranslation('offers')
  const { t: td } = useTranslation('dashboard')
  const f = useFormat()
  const ref = useRef<HTMLDivElement>(null)
  useFlip(ref, version)

  return (
    <div ref={ref} className={styles.list}>
      {offers.map((offer, index) => (
        <div key={offer.dishId} data-flip-key={offer.dishId} className={styles.item}>
          <HotOfferRow
            rank={index + 1}
            dishName={offer.dishName}
            verdict={offerVerdict(offer.score)}
            verdictText={f.verdict(offer.score)}
            reason={reasonWithStock(offerReason(offer, f, td), offer, t('list.canCook', { portions: f.portionsEnoughFor(offer.maxPortions) }))}
            calculation={
              <ul className={styles.calculation}>
                {offerCalculation(offer, f, td).map((line) => (
                  <li key={line}>{line}</li>
                ))}
              </ul>
            }
          />
        </div>
      ))}
    </div>
  )
}

/** «Сейчас хватит на N порций» — только если это больше, чем «получится до N» в причине, иначе повтор. */
function reasonWithStock(reason: string, offer: HotOffer, canCook: string) {
  const burned = Math.max(0, ...offer.reasons.map((r) => r.portionsBurned))
  return offer.maxPortions > burned ? `${reason} ${canCook}` : reason
}

/** Как выбираются блюда — простыми словами, без формул (формула — в «Как это посчитано» у строки). */
function HowItWorks() {
  const { t } = useTranslation('offers')
  const steps = ['spoiling', 'uses', 'canCook', 'live'] as const
  return (
    <Panel title={t('how.title')} titleId="offers-how">
      <ol className={styles.steps}>
        {steps.map((step, index) => (
          <li key={step} className={styles.step}>
            <span className={styles.stepNumber} aria-hidden="true">
              {index + 1}
            </span>
            <p className={styles.stepText}>{t(`how.${step}`)}</p>
          </li>
        ))}
      </ol>
    </Panel>
  )
}

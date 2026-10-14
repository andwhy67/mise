import { useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useHotMenu } from '@/api/queries'
import type { HotMenuItem, HotOffer } from '@/api/types'
import { HotOfferRow } from '@/components/HotOfferRow'
import { LiveIndicator } from '@/components/LiveIndicator'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { ScrollArea } from '@/components/ScrollArea'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { offerVerdict } from '@/lib/format'
import { offerCalculation, offerReason } from '@/lib/texts'
import { useFlip } from '@/lib/useFlip'
import { useFormat } from '@/lib/useFormat'
import styles from './HotOffersPage.module.scss'

/** Порций или меньше — блюдо скоро упрётся в остаток, предлагать осторожно. */
const FEW_PORTIONS = 5

/**
 * Что продавать (DESIGN.md §6.3): всё доступное меню по порядку «что предлагать».
 * Сначала блюда, которые спасают продукты; затем те, что разгружают склад; затем остальное — так ответ есть всегда,
 * даже когда ничего не горит.
 */
export function HotOffersPage() {
  const { t } = useTranslation('offers')
  const f = useFormat()
  const query = useHotMenu()
  const items = query.data?.items ?? []
  const saves = items.filter((i) => i.group === 'Saves' && i.offer)
  const overstock = items.filter((i) => i.group === 'Overstock')
  const regular = items.filter((i) => i.group === 'Regular')

  return (
    <Page
      title={t('title')}
      subtitle={query.data ? t('updated', { time: f.time(query.data.generatedAt) }) : t('subtitle')}
      actions={<LiveIndicator />}
    >
      {query.isPending ? (
        <LoadingState lines={6} />
      ) : query.isError ? (
        <Panel>
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </Panel>
      ) : (
        <div className={styles.columns}>
          <div className={styles.main}>
            <Panel title={t('saves.title')} subtitle={t('saves.subtitle')} titleId="offers-saves">
              {saves.length === 0 ? (
                <EmptyState title={t('saves.empty')}>{overstock.length ? t('saves.emptyHintOverstock') : t('saves.emptyHint')}</EmptyState>
              ) : (
                <OfferList offers={saves.map((i) => i.offer!)} version={query.data.generatedAt} />
              )}
            </Panel>
            {overstock.length > 0 && (
              <Panel title={t('overstock.title')} subtitle={t('overstock.subtitle')} titleId="offers-overstock">
                <ul className={styles.simpleList}>
                  {overstock.map((item) => (
                    <OverstockRow key={item.dishId} item={item} />
                  ))}
                </ul>
              </Panel>
            )}
            <Panel title={t('regular.title')} subtitle={t('regular.subtitle')} titleId="offers-regular">
              {regular.length === 0 ? (
                <EmptyState title={t('regular.empty')} />
              ) : (
                <ScrollArea label={t('regular.title')} size="half">
                  <ul className={styles.simpleList}>
                    {regular.map((item) => (
                      <RegularRow key={item.dishId} item={item} />
                    ))}
                  </ul>
                </ScrollArea>
              )}
            </Panel>
          </div>
          <HowItWorks />
        </div>
      )}
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

/** Разгрузить склад: какого продукта много и на сколько дней обычного спроса его хватит. */
function OverstockRow({ item }: { item: HotMenuItem }) {
  const { t } = useTranslation('offers')
  const f = useFormat()
  const cover = item.cover!
  return (
    <li className={styles.simpleRow}>
      <div className={styles.simpleMain}>
        <h3 className={styles.dish}>{item.dishName}</h3>
        <StatusMark tone="info">{t('overstock.verdict')}</StatusMark>
        <p className={styles.reason}>
          {t('overstock.reason', {
            product: cover.productName,
            qty: f.qty(cover.usableQty, cover.unit),
            days: f.number(cover.coverDays, 0),
            portions: f.portionsEnoughFor(item.maxPortions),
          })}
        </p>
      </div>
      <span className={styles.price}>{f.money(item.price)}</span>
    </li>
  )
}

/** Остальное меню: можно предлагать спокойно; мало порций — предупреждаем. */
function RegularRow({ item }: { item: HotMenuItem }) {
  const f = useFormat()
  return (
    <li className={styles.compactRow}>
      <span className={styles.compactDish}>{item.dishName}</span>
      <StatusMark tone={item.maxPortions <= FEW_PORTIONS ? 'warn' : 'fresh'} weight="regular">
        {f.portionsEnoughFor(item.maxPortions)}
      </StatusMark>
      <span className={styles.price}>{f.money(item.price)}</span>
    </li>
  )
}

/** Как выбираются блюда — простыми словами, без формул (формула — в «Как это посчитано» у строки). */
function HowItWorks() {
  const { t } = useTranslation('offers')
  const steps = ['spoiling', 'uses', 'overstock', 'canCook', 'live'] as const
  return (
    <Panel title={t('how.title')} titleId="offers-how" className={styles.how}>
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

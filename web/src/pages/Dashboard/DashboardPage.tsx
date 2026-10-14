import { Plus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useExpiringBatches, useHotMenu, useHotOffers, useKpi, useStopList, useStopListForecast, useSupplySuggestions } from '@/api/queries'
import { useSession, type UserRole } from '@/auth/session'
import { LinkButton } from '@/components/Button'
import { ExpiryList } from '@/components/ExpiryList'
import { HotOfferRow } from '@/components/HotOfferRow'
import { LiveIndicator } from '@/components/LiveIndicator'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { StopListRow } from '@/components/StopListRow'
import { SummaryStrip, type SummaryItem } from '@/components/SummaryStrip'
import { offerVerdict } from '@/lib/format'
import { useFormat } from '@/lib/useFormat'
import { expiryItems, forecastReason, offerCalculation, offerReason, stopReason, supplyLine } from '@/lib/texts'
import styles from './DashboardPage.module.scss'

/** Окно «скоро испортится» на Главной, часы (DESIGN.md §1.1: янтарный — ближайшие 48 часов). */
const EXPIRY_WINDOW_HOURS = 48
/** «Скоро закончится» — прогноз стоп-листа на ближайшие часы. */
const SOON_HOURS = 6
const ROWS = 5

/** Главная (DESIGN.md §6.2): отвечает, что продавать, что нельзя заказать и что скоро испортится. */
export function DashboardPage() {
  const { t } = useTranslation('dashboard')
  const f = useFormat()
  const role = useSession().user?.role ?? 'Chef'
  const sells = role !== 'Warehouse'

  return (
    <Page
      title={t('title')}
      subtitle={f.todayLine()}
      actions={
        <div className={styles.headerActions}>
          <LiveIndicator />
          {sells ? (
            <LinkButton to="/orders" icon={<Plus />}>
              {t('takeOrder')}
            </LinkButton>
          ) : (
            <LinkButton to="/supplies" icon={<Plus />}>
              {t('takeSupply')}
            </LinkButton>
          )}
        </div>
      }
    >
      <Summary role={role} />

      <div className={styles.columns}>
        {sells ? <OffersPanel /> : <ToOrderPanel />}
        <StopPanel />
      </div>

      <ExpiryPanel />
    </Page>
  )
}

function Summary({ role }: { role: UserRole }) {
  const { t } = useTranslation('dashboard')
  const stop = useStopList()
  const expiring = useExpiringBatches(EXPIRY_WINDOW_HOURS)
  const sells = role !== 'Warehouse'
  const kpi = useKpi(sells)
  const suggestions = useSupplySuggestions(!sells)

  const stopCount = stop.data?.length
  const expiringCount = expiring.data ? new Set(expiring.data.filter((b) => b.remainingQty > 0).map((b) => b.productId)).size : undefined
  const items: SummaryItem[] = [
    {
      id: 'stop',
      value: stopCount ?? '—',
      tone: stopCount ? 'crit' : 'fresh',
      phrase: stopCount === 0 ? t('summary.stopNone') : t('summary.stop', { count: stopCount ?? 0 }),
      linkLabel: t('summary.stopLink'),
      to: '/stoplist',
    },
    {
      id: 'expiring',
      value: expiringCount ?? '—',
      tone: expiringCount ? 'warn' : 'fresh',
      phrase: t('summary.expiring', { count: expiringCount ?? 0 }),
      linkLabel: t('summary.expiringLink'),
      to: '/stock?show=expiring',
    },
    sells
      ? {
          id: 'orders',
          value: kpi.data?.now.ordersToday ?? '—',
          tone: 'neutral',
          phrase: t('summary.orders', { count: kpi.data?.now.ordersToday ?? 0 }),
          linkLabel: t('summary.ordersLink'),
          to: '/orders',
        }
      : {
          id: 'to-order',
          value: suggestions.data?.items.length ?? '—',
          tone: suggestions.data?.items.length ? 'warn' : 'neutral',
          phrase: t('summary.toOrder', { count: suggestions.data?.items.length ?? 0 }),
          linkLabel: t('summary.toOrderLink'),
          to: '/supplies',
        },
  ]
  return <SummaryStrip items={items} label={t('summaryLabel')} />
}

function OffersPanel() {
  const { t } = useTranslation('dashboard')
  const f = useFormat()
  const query = useHotOffers()
  const offers = query.data?.offers.slice(0, 3) ?? []

  return (
    <Panel title={t('offers.title')} subtitle={t('offers.subtitle')} titleId="dash-offers" className={styles.wide}>
      {query.isPending ? (
        <LoadingState />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : offers.length === 0 ? (
        <CalmOffers />
      ) : (
        offers.map((offer, index) => (
          <HotOfferRow
            key={offer.dishId}
            rank={index + 1}
            dishName={offer.dishName}
            verdict={offerVerdict(offer.score)}
            verdictText={f.verdict(offer.score)}
            reason={offerReason(offer, f, t)}
            calculation={
              <ul className={styles.calculation}>
                {offerCalculation(offer, f, t).map((line) => (
                  <li key={line}>{line}</li>
                ))}
              </ul>
            }
          />
        ))
      )}
    </Panel>
  )
}

/**
 * Когда ничего не горит, Главная всё равно отвечает «что предлагать»: блюда, которые разгружают склад,
 * а если таких нет — те, что можно приготовить больше всего (ранжирование /hot-offers/menu).
 */
function CalmOffers() {
  const { t } = useTranslation('dashboard')
  const { t: to } = useTranslation('offers')
  const f = useFormat()
  const menu = useHotMenu()
  const picks = (menu.data?.items ?? []).filter((i) => i.group !== 'Saves').slice(0, 3)
  if (picks.length === 0) return <EmptyState title={t('offers.empty')}>{t('offers.emptyHint')}</EmptyState>
  return (
    <>
      <p className={styles.calm}>{t('offers.calm')}</p>
      <ul className={styles.calmList}>
        {picks.map((item) => (
          <li key={item.dishId} className={styles.calmRow}>
            <h3 className={styles.calmDish}>{item.dishName}</h3>
            <StatusMark tone={item.group === 'Overstock' ? 'info' : 'fresh'} weight="regular">
              {item.cover
                ? to('overstock.reason', {
                    product: item.cover.productName,
                    qty: f.qty(item.cover.usableQty, item.cover.unit),
                    days: f.number(item.cover.coverDays, 0),
                    portions: f.portionsEnoughFor(item.maxPortions),
                  })
                : f.portionsEnoughFor(item.maxPortions)}
            </StatusMark>
          </li>
        ))}
      </ul>
    </>
  )
}

function ToOrderPanel() {
  const { t } = useTranslation('dashboard')
  const f = useFormat()
  const query = useSupplySuggestions()
  const items = query.data?.items.slice(0, ROWS) ?? []
  const tone = { Critical: 'crit', High: 'warn', Normal: 'info' } as const

  return (
    <Panel title={t('toOrder.title')} subtitle={t('toOrder.subtitle')} titleId="dash-to-order" className={styles.wide}>
      {query.isPending ? (
        <LoadingState />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t('toOrder.empty')}>{t('toOrder.emptyHint')}</EmptyState>
      ) : (
        <ul className={styles.orderList}>
          {items.map((item) => (
            <li key={item.productId} className={styles.orderRow}>
              <h3 className={styles.orderProduct}>{item.productName}</h3>
              <StatusMark tone={tone[item.urgency]}>{t('toOrder.quantity', { qty: f.qty(item.suggestedQty, item.unit) })}</StatusMark>
              <p className={styles.orderReason}>{supplyLine(item, f, t)}</p>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

function StopPanel() {
  const { t } = useTranslation('dashboard')
  const f = useFormat()
  const stop = useStopList()
  const forecast = useStopListForecast(SOON_HOURS)
  const stopped = new Set(stop.data?.map((s) => s.dishId))
  const soon = (forecast.data?.items ?? []).filter((item) => !stopped.has(item.dishId))
  const out = stop.data ?? []

  return (
    <Panel title={t('stop.title')} titleId="dash-stop">
      {stop.isPending ? (
        <LoadingState />
      ) : stop.isError ? (
        <ErrorState error={stop.error} onRetry={() => void stop.refetch()} />
      ) : out.length + soon.length === 0 ? (
        <EmptyState title={t('stop.empty')}>{t('stop.emptyHint')}</EmptyState>
      ) : (
        <>
          {out.slice(0, ROWS).map((item) => (
            <StopListRow key={item.dishId} group="out" groupLabel={t('stop.out')} dishName={item.dishName} reason={stopReason(item, f, t)} />
          ))}
          {soon.slice(0, Math.max(0, ROWS - out.length)).map((item) => (
            <StopListRow key={item.dishId} group="soon" groupLabel={t('stop.soon')} dishName={item.dishName} reason={forecastReason(item, f, t)} />
          ))}
        </>
      )}
    </Panel>
  )
}

function ExpiryPanel() {
  const { t } = useTranslation('dashboard')
  const f = useFormat()
  const query = useExpiringBatches(EXPIRY_WINDOW_HOURS)
  const items = query.data ? expiryItems(query.data, f).slice(0, ROWS) : []

  return (
    <Panel
      title={t('expiry.title')}
      subtitle={t('expiry.subtitle')}
      titleId="dash-expiry"
      action={
        <LinkButton to="/stock" variant="text">
          {t('expiry.showAll')}
        </LinkButton>
      }
    >
      {query.isPending ? (
        <LoadingState />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t('expiry.empty')}>{t('expiry.emptyHint')}</EmptyState>
      ) : (
        <ExpiryList items={items} label={t('expiry.title')} />
      )}
    </Panel>
  )
}

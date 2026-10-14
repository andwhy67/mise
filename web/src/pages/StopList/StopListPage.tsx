import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSetDishActive } from '@/api/mutations'
import { useMenu, useStopList, useStopListForecast, useStopListHistory } from '@/api/queries'
import type { StopListForecastItem, StopListItem } from '@/api/types'
import { useSession } from '@/auth/session'
import { Button } from '@/components/Button'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { Sheet } from '@/components/Sheet'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { stopReason } from '@/lib/texts'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import styles from './StopListPage.module.scss'

/** Горизонт «скоро закончится» на этом экране, часы — шире, чем на Главной. */
const FORECAST_HOURS = 12

/** Стоп-лист (DESIGN.md §6.3): что нельзя заказать сейчас, что скоро закончится и как было раньше. */
export function StopListPage() {
  const { t } = useTranslation('stoplist')
  const isManager = useSession().user?.role === 'Manager'
  const [removing, setRemoving] = useState(false)

  return (
    <Page
      title={t('title')}
      subtitle={t('subtitle')}
      actions={
        isManager && (
          <Button variant="secondary" onClick={() => setRemoving(true)}>
            {t('manual.open')}
          </Button>
        )
      }
    >
      <div className={styles.columns}>
        <NowPanel />
        <SoonPanel />
      </div>
      <HistoryPanel />
      {isManager && <RemoveDishSheet open={removing} onClose={() => setRemoving(false)} />}
    </Page>
  )
}

function NowPanel() {
  const { t } = useTranslation('stoplist')
  const { t: td } = useTranslation('dashboard')
  const f = useFormat()
  const query = useStopList()
  const isManager = useSession().user?.role === 'Manager'
  const items = [...(query.data ?? [])].sort((a, b) => (a.since ?? '').localeCompare(b.since ?? ''))

  return (
    <Panel title={t('now.title')} subtitle={t('now.subtitle')} titleId="stop-now">
      {query.isPending ? (
        <LoadingState />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t('now.empty')}>{t('now.emptyHint')}</EmptyState>
      ) : (
        <ul className={styles.list}>
          {items.map((item) => (
            <li key={item.dishId} className={styles.row}>
              <p className={`${styles.group} ${styles.out}`}>{item.reason === 'Manual' ? t('now.manual') : t('now.shortage')}</p>
              <h3 className={styles.dish}>{item.dishName}</h3>
              {item.reason !== 'Manual' && <p className={styles.reason}>{stopReason(item, f, td)}</p>}
              {item.since && <p className={styles.meta}>{t('now.since', { when: f.moment(item.since) })}</p>}
              {isManager && item.reason === 'Manual' && <ReturnButton item={item} />}
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

function ReturnButton({ item }: { item: StopListItem }) {
  const { t } = useTranslation('stoplist')
  const errorText = useErrorText()
  const mutation = useSetDishActive()
  return (
    <Button
      variant="text"
      className={styles.action}
      disabled={mutation.isPending}
      onClick={() =>
        mutation.mutate(
          { dishId: item.dishId, isActive: true },
          {
            onSuccess: () => toast.show(t('manual.returned', { dish: item.dishName })),
            onError: (cause) => toast.show(errorText(cause), 'crit'),
          },
        )
      }
    >
      {t('manual.return')}
    </Button>
  )
}

interface ForecastGroup {
  productId: string
  productName: string
  eta: string
  item: StopListForecastItem
  dishes: string[]
}

/** Блюда с одним ограничивающим продуктом — одной строкой: «Моцарелла: хватит примерно на 4 часа → Капрезе, Пицца». */
function groupByProduct(items: StopListForecastItem[]): ForecastGroup[] {
  const groups = new Map<string, ForecastGroup>()
  for (const item of items) {
    const key = `${item.limitingProduct.productId}:${item.cause}`
    const group = groups.get(key)
    if (group) {
      group.dishes.push(item.dishName)
      if (item.eta < group.eta) Object.assign(group, { eta: item.eta, item })
    } else {
      groups.set(key, { productId: item.limitingProduct.productId, productName: item.limitingProduct.productName, eta: item.eta, item, dishes: [item.dishName] })
    }
  }
  return [...groups.values()].sort((a, b) => a.eta.localeCompare(b.eta))
}

function SoonPanel() {
  const { t } = useTranslation('stoplist')
  const f = useFormat()
  const stop = useStopList()
  const forecast = useStopListForecast(FORECAST_HOURS)
  const stopped = new Set(stop.data?.map((s) => s.dishId))
  const groups = groupByProduct((forecast.data?.items ?? []).filter((item) => !stopped.has(item.dishId)))

  return (
    <Panel title={t('soon.title')} subtitle={t('soon.subtitle', { hours: FORECAST_HOURS })} titleId="stop-soon">
      {forecast.isPending ? (
        <LoadingState />
      ) : forecast.isError ? (
        <ErrorState error={forecast.error} onRetry={() => void forecast.refetch()} />
      ) : groups.length === 0 ? (
        <EmptyState title={t('soon.empty')}>{t('soon.emptyHint')}</EmptyState>
      ) : (
        <ul className={styles.list}>
          {groups.map((group) => (
            <li key={`${group.productId}:${group.item.cause}`} className={styles.row}>
              <p className={`${styles.group} ${styles.soon}`}>
                {group.item.cause === 'Expiry' ? t('soon.expiry', { product: group.productName }) : t('soon.consumption', { product: group.productName })}
              </p>
              <h3 className={styles.dish}>{group.dishes.join(', ')}</h3>
              <StatusMark tone={group.item.hoursLeft < 2 ? 'crit' : 'warn'} weight="regular">
                {group.item.cause === 'Expiry'
                  ? t('soon.until', { when: f.whenInline(group.eta) })
                  : `${f.runway(group.item.hoursLeft)} (${t('soon.until', { when: f.whenInline(group.eta) })})`}
              </StatusMark>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

function HistoryPanel() {
  const { t } = useTranslation('stoplist')
  const f = useFormat()
  const query = useStopListHistory()
  const items = query.data?.items ?? []

  return (
    <Panel title={t('history.title')} subtitle={t('history.subtitle')} titleId="stop-history">
      {query.isPending ? (
        <LoadingState />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t('history.empty')} />
      ) : (
        <ul className={styles.history}>
          {items.map((item) => (
            <li key={item.id} className={styles.historyRow}>
              <span className={styles.historyDish}>{item.dishName}</span>
              <span className={styles.historyReason}>
                {item.reason === 'Manual' ? t('history.manual') : t('history.shortage', { product: item.limitingProductName ?? '—' })}
              </span>
              <span className={styles.historyWhen}>
                {item.endedAt
                  ? t('history.period', {
                      from: f.moment(item.startedAt),
                      // Тот же день — достаточно времени; иначе конец тоже с датой, чтобы «до 17:57» не путало.
                      to: f.date(item.startedAt) === f.date(item.endedAt) ? f.time(item.endedAt) : f.moment(item.endedAt),
                      duration: f.duration(item.durationMinutes ?? 0),
                    })
                  : t('history.ongoing', { from: f.moment(item.startedAt) })}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

/** Менеджер убирает блюдо из меню вручную (поломка, нет повара, решение шефа) — оно уходит в стоп-лист. */
function RemoveDishSheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation('stoplist')
  const errorText = useErrorText()
  const menu = useMenu()
  const mutation = useSetDishActive()
  const [dishId, setDishId] = useState<string | null>(null)
  const name = useId()
  const dishes = (menu.data ?? []).filter((d) => d.isActive).sort((a, b) => a.dishName.localeCompare(b.dishName))
  const chosen = dishes.find((d) => d.dishId === dishId)

  const close = () => {
    setDishId(null)
    onClose()
  }

  return (
    <Sheet
      open={open}
      onClose={close}
      title={t('manual.title')}
      subtitle={t('manual.subtitle')}
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            {t('manual.cancel')}
          </Button>
          <Button
            disabled={!chosen || mutation.isPending}
            onClick={() =>
              chosen &&
              mutation.mutate(
                { dishId: chosen.dishId, isActive: false },
                {
                  onSuccess: () => {
                    toast.show(t('manual.removed', { dish: chosen.dishName }), 'warn')
                    close()
                  },
                  onError: (cause) => toast.show(errorText(cause), 'crit'),
                },
              )
            }
          >
            {t('manual.submit')}
          </Button>
        </>
      }
    >
      {menu.isPending ? (
        <LoadingState />
      ) : menu.isError ? (
        <ErrorState error={menu.error} onRetry={() => void menu.refetch()} />
      ) : (
        <fieldset className={styles.choices}>
          <legend className="visually-hidden">{t('manual.title')}</legend>
          {dishes.map((dish) => (
            <label key={dish.dishId} className={styles.choice}>
              <input type="radio" name={name} checked={dishId === dish.dishId} onChange={() => setDishId(dish.dishId)} />
              <span>{dish.dishName}</span>
            </label>
          ))}
        </fieldset>
      )}
    </Sheet>
  )
}

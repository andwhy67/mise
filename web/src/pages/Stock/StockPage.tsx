import type { TFunction } from 'i18next'
import { ChevronDown, Search } from 'lucide-react'
import { useId, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { useProductBatches, useStock } from '@/api/queries'
import type { Batch, StockItem } from '@/api/types'
import { useSession } from '@/auth/session'
import { Button } from '@/components/Button'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { Segmented } from '@/components/Segmented'
import { Meter } from '@/components/Meter'
import { ScrollArea } from '@/components/ScrollArea'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import type { Formatter } from '@/lib/format'
import { useFormat } from '@/lib/useFormat'
import { WriteOffSheet } from './WriteOffSheet'
import styles from './StockPage.module.scss'

export type StockFilter = 'all' | 'expiring' | 'low' | 'out' | 'spoiled'
const FILTERS: StockFilter[] = ['all', 'expiring', 'low', 'out', 'spoiled']

const matches: Record<StockFilter, (item: StockItem) => boolean> = {
  all: () => true,
  expiring: (item) => item.status === 'Warning' || item.status === 'Critical',
  low: (item) => item.belowMinStock && item.usableQty > 0,
  out: (item) => item.status === 'OutOfStock',
  spoiled: (item) => item.expiredQty > 0,
}

/** Сначала то, что надо разобрать: испортившееся, потом по ближайшему сроку; закончившееся — в конце. */
function compare(a: StockItem, b: StockItem) {
  if (a.expiredQty > 0 !== b.expiredQty > 0) return a.expiredQty > 0 ? -1 : 1
  if (a.nearestExpiresAt && b.nearestExpiresAt) return a.nearestExpiresAt.localeCompare(b.nearestExpiresAt)
  if (a.nearestExpiresAt || b.nearestExpiresAt) return a.nearestExpiresAt ? -1 : 1
  return a.productName.localeCompare(b.productName)
}

/** Склад (DESIGN.md §6.3): что есть, когда испортится, из каких поставок и в каком порядке уйдёт в работу. */
export function StockPage() {
  const { t } = useTranslation('stock')
  const query = useStock()
  const [params, setParams] = useSearchParams()
  const shown = params.get('show') as StockFilter | null
  const filter: StockFilter = shown && FILTERS.includes(shown) ? shown : 'all'
  const [search, setSearch] = useState('')
  const searchId = useId()

  const all = useMemo(() => query.data ?? [], [query.data])
  const needle = search.trim().toLocaleLowerCase()
  const items = all
    .filter(matches[filter])
    .filter((item) => !needle || item.productName.toLocaleLowerCase().includes(needle))
    .sort(compare)

  return (
    <Page title={t('title')} subtitle={t('subtitle')}>
      <div className={styles.controls}>
        <Segmented
          label={t('filter.label')}
          value={filter}
          options={FILTERS.map((value) => ({
            value,
            label: t(`filter.${value}`),
            count: query.data ? all.filter(matches[value]).length : undefined,
          }))}
          onChange={(value) => setParams(value === 'all' ? {} : { show: value }, { replace: true })}
        />
        <div className={styles.search}>
          <label htmlFor={searchId} className="visually-hidden">
            {t('search')}
          </label>
          <Search aria-hidden="true" strokeWidth={1.8} />
          <input id={searchId} type="search" placeholder={t('search')} value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
      </div>

      <Panel>
        {query.isPending ? (
          <LoadingState lines={6} />
        ) : query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : items.length === 0 ? (
          <EmptyState title={t(needle ? 'empty.search' : `empty.${filter}`)}>{t('empty.hint')}</EmptyState>
        ) : (
          <ScrollArea label={t('title')}>
            <ul className={styles.list} aria-label={t('title')}>
              {items.map((item) => (
                <StockRow key={item.productId} item={item} />
              ))}
            </ul>
          </ScrollArea>
        )}
      </Panel>
    </Page>
  )
}

function StockRow({ item }: { item: StockItem }) {
  const { t } = useTranslation('stock')
  const f = useFormat()
  const [open, setOpen] = useState(false)
  const detailsId = useId()
  const when = item.nearestExpiresAt && item.status !== 'OutOfStock' ? f.when(item.nearestExpiresAt, item.status) : null

  return (
    <li className={styles.row}>
      <button type="button" className={styles.summary} aria-expanded={open} aria-controls={detailsId} onClick={() => setOpen(!open)}>
        <span className={styles.name}>
          <span className={styles.product}>{item.productName}</span>
          {item.category && <span className={styles.category}>{item.category}</span>}
        </span>
        <span className={styles.qty}>
          <span className={styles.amount}>{item.usableQty > 0 ? f.qty(item.usableQty, item.unit) : t('none')}</span>
          {item.belowMinStock && item.usableQty > 0 && (
            <StatusMark tone="warn" weight="regular">
              {t('low', { min: f.qty(item.minStockQty, item.unit) })}
            </StatusMark>
          )}
        </span>
        <span className={styles.when}>
          {item.status === 'OutOfStock' ? <StatusMark tone="crit">{t('outOfStock')}</StatusMark> : when && <StatusMark tone={when.tone}>{when.text}</StatusMark>}
          {item.expiredQty > 0 && (
            <StatusMark tone="expired" weight="regular">
              {t('spoiledQty', { qty: f.qty(item.expiredQty, item.unit) })}
            </StatusMark>
          )}
        </span>
        <ChevronDown className={styles.chevron} aria-hidden="true" strokeWidth={1.8} />
      </button>
      <div id={detailsId} className={styles.details} hidden={!open}>
        {open && <ProductDetails item={item} />}
      </div>
    </li>
  )
}

function ProductDetails({ item }: { item: StockItem }) {
  const { t } = useTranslation('stock')
  const f = useFormat()
  const role = useSession().user?.role
  const canWriteOff = role === 'Warehouse' || role === 'Manager'
  const query = useProductBatches(item.productId)
  const [writeOff, setWriteOff] = useState<Batch | null>(null)

  if (query.isPending) return <LoadingState lines={2} />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  const batches = query.data.batches.filter((b) => b.remainingQty > 0)
  if (batches.length === 0) return <p className={styles.note}>{t('details.empty')}</p>

  return (
    <>
      <p className={styles.note}>{t('details.order')}</p>
      <ol className={styles.batches}>
        {batches.map((batch) => (
          <li key={batch.id} className={styles.batch}>
            <div className={styles.batchHead}>
              <span className={styles.batchTitle}>{t('details.supply', { date: f.date(batch.receivedAt) })}</span>
              <span className={styles.batchMeta}>{batch.supplyNumber}</span>
            </div>
            <p className={styles.batchQty}>{queueText(batch, f, t)}</p>
            {/* Две подписанные шкалы: сколько осталось от поставки и сколько осталось от срока. Цифры — рядом. */}
            <Meter
              label={t('details.remaining')}
              value={t('details.left', { left: f.qty(batch.remainingQty, batch.unit), of: f.qty(batch.initialQty, batch.unit) })}
              fraction={batch.initialQty > 0 ? batch.remainingQty / batch.initialQty : 0}
              tone="info"
            />
            <ExpiryMeter batch={batch} />
            {canWriteOff && (
              <Button variant="text" className={styles.batchAction} onClick={() => setWriteOff(batch)}>
                {t('writeOff.action')}
              </Button>
            )}
          </li>
        ))}
      </ol>
      <WriteOffSheet batch={writeOff} onClose={() => setWriteOff(null)} />
    </>
  )
}

/** Шкала срока: сколько осталось от полного срока годности; подпись — словами («Завтра в 03:30»). */
function ExpiryMeter({ batch }: { batch: Batch }) {
  const { t } = useTranslation('stock')
  const f = useFormat()
  const when = f.when(batch.expiresAt, batch.isUsable ? batch.shelfLife.status : 'Expired')
  return <Meter label={t('details.expiry')} value={when.text} fraction={batch.isUsable ? batch.shelfLife.remainingFraction : 0} tone={when.tone} />
}

function queueText(batch: Batch, f: Formatter, t: TFunction<'stock'>) {
  if (!batch.isUsable) return t('details.notUsable')
  if (batch.fefoRank === 1) return t('details.first')
  return t('details.queue', { rank: f.number(batch.fefoRank ?? 0) })
}

import { ChevronDown, Plus } from 'lucide-react'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSupplies, useSupply, useSupplySuggestions } from '@/api/queries'
import type { SupplyListItem, SupplySuggestion } from '@/api/types'
import { Button } from '@/components/Button'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { supplyLine } from '@/lib/texts'
import { useFormat } from '@/lib/useFormat'
import { SupplyFormSheet, type PrefillRow } from './SupplyFormSheet'
import styles from './SuppliesPage.module.scss'

const PAGE = 20
const MAX_PAGE = 100
const URGENCY_TONE = { Critical: 'crit', High: 'warn', Normal: 'info' } as const

/** Поставки (DESIGN.md §6.3): приём поставки, что заказать, история приёмок. */
export function SuppliesPage() {
  const { t } = useTranslation('supplies')
  const [form, setForm] = useState<{ prefill?: PrefillRow[] } | null>(null)
  const open = (prefill?: PrefillRow[]) => setForm({ prefill })

  return (
    <Page
      title={t('title')}
      subtitle={t('subtitle')}
      actions={
        <Button icon={<Plus />} onClick={() => open()}>
          {t('receive')}
        </Button>
      }
    >
      <div className={styles.columns}>
        <SuppliesList />
        <SuggestionsPanel onAdd={open} />
      </div>
      {/* key: каждое открытие — свежая форма со своим номером и временем. */}
      <SupplyFormSheet key={form ? 'open' : 'closed'} open={form !== null} prefill={form?.prefill} onClose={() => setForm(null)} />
    </Page>
  )
}

function SuggestionsPanel({ onAdd }: { onAdd: (rows: PrefillRow[]) => void }) {
  const { t } = useTranslation('supplies')
  const { t: td } = useTranslation('dashboard')
  const f = useFormat()
  const query = useSupplySuggestions()
  const items = query.data?.items ?? []
  const urgent = items.filter((i) => i.urgency !== 'Normal')
  const toRow = (item: SupplySuggestion): PrefillRow => ({ productId: item.productId, quantity: item.suggestedQty })

  return (
    <Panel
      title={t('suggestions.title')}
      subtitle={t('suggestions.subtitle', { hours: query.data?.horizonHours ?? 72 })}
      titleId="supplies-suggestions"
      action={
        urgent.length > 1 && (
          <Button variant="text" onClick={() => onAdd(urgent.map(toRow))}>
            {t('suggestions.addUrgent', { count: urgent.length })}
          </Button>
        )
      }
    >
      {query.isPending ? (
        <LoadingState />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={td('toOrder.empty')}>{td('toOrder.emptyHint')}</EmptyState>
      ) : (
        <ul className={styles.suggestions}>
          {items.map((item) => (
            <li key={item.productId} className={styles.suggestion}>
              <div className={styles.suggestionBody}>
                <h3 className={styles.suggestionProduct}>{item.productName}</h3>
                <StatusMark tone={URGENCY_TONE[item.urgency]}>{td('toOrder.quantity', { qty: f.qty(item.suggestedQty, item.unit) })}</StatusMark>
                <p className={styles.suggestionReason}>{supplyLine(item, f, td)}</p>
              </div>
              <Button variant="text" className={styles.suggestionAdd} onClick={() => onAdd([toRow(item)])} aria-label={t('suggestions.addLabel', { product: item.productName })}>
                {t('suggestions.add')}
              </Button>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

function SuppliesList() {
  const { t } = useTranslation('supplies')
  const [size, setSize] = useState(PAGE)
  const query = useSupplies(size)
  const items = query.data?.items ?? []

  return (
    <Panel title={t('list.title')} subtitle={t('list.subtitle')} titleId="supplies-list">
      {query.isPending ? (
        <LoadingState lines={5} />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t('list.empty')}>{t('list.emptyHint')}</EmptyState>
      ) : (
        <>
          <ul className={styles.list}>
            {items.map((supply) => (
              <SupplyRow key={supply.id} supply={supply} />
            ))}
          </ul>
          {query.data && query.data.total > items.length && size < MAX_PAGE && (
            <Button variant="text" className={styles.more} onClick={() => setSize(Math.min(MAX_PAGE, size + PAGE))}>
              {t('list.more')}
            </Button>
          )}
        </>
      )}
    </Panel>
  )
}

function SupplyRow({ supply }: { supply: SupplyListItem }) {
  const { t } = useTranslation('supplies')
  const f = useFormat()
  const [open, setOpen] = useState(false)
  const detailsId = useId()

  return (
    <li className={styles.row}>
      <button type="button" className={styles.toggle} aria-expanded={open} aria-controls={detailsId} onClick={() => setOpen(!open)}>
        <span className={styles.main}>
          <span className={styles.supplier}>{supply.supplier || t('list.noSupplier')}</span>
          <span className={styles.meta}>
            {f.moment(supply.createdAt)} · {supply.number}
          </span>
        </span>
        <span className={styles.count}>{t('list.items', { count: supply.itemCount })}</span>
        <ChevronDown className={styles.chevron} aria-hidden="true" strokeWidth={1.8} />
      </button>
      <div id={detailsId} className={styles.details} hidden={!open}>
        {open && <SupplyDetails id={supply.id} />}
      </div>
    </li>
  )
}

function SupplyDetails({ id }: { id: string }) {
  const { t } = useTranslation('supplies')
  const f = useFormat()
  const query = useSupply(id)
  if (query.isPending) return <LoadingState lines={2} />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />

  return (
    <ul className={styles.items}>
      {query.data.items.map((item) => {
        const expired = item.batchStatus === 'Expired' || new Date(item.expiresAt) <= new Date()
        return (
          <li key={item.id} className={styles.item}>
            <span className={styles.itemProduct}>{item.productName}</span>
            <span className={styles.itemQty}>{f.qty(item.quantity, item.unit)}</span>
            <span className={styles.itemExpiry}>
              {expired ? (
                <StatusMark tone="expired" weight="regular">
                  {t('details.expired')}
                </StatusMark>
              ) : (
                t('details.until', { when: f.moment(item.expiresAt) })
              )}
              <span className={styles.itemSource}>{t(`details.source.${item.expirySource}`, { defaultValue: '' })}</span>
            </span>
          </li>
        )
      })}
    </ul>
  )
}

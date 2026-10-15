import { ChevronDown } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useCancelOrder, useOrderStatus } from '@/api/mutations'
import { useOrder, useOrders } from '@/api/queries'
import type { OrderListItem, OrderStatus } from '@/api/types'
import { useSession } from '@/auth/session'
import { Button } from '@/components/Button'
import { Panel } from '@/components/Panel'
import { ScrollArea } from '@/components/ScrollArea'
import { Segmented } from '@/components/Segmented'
import { Sheet } from '@/components/Sheet'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import type { Tone } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import styles from './RecentOrders.module.scss'

const PAGE = 30
const MAX_PAGE = 100

const STATUS_TONE: Record<OrderStatus, Tone> = { Created: 'info', Cooking: 'warn', Done: 'fresh', Served: 'muted', Cancelled: 'expired' }
/** Следующий шаг заказа: Принят → Готовится → Готов → Выдан. */
const NEXT: Partial<Record<OrderStatus, OrderStatus>> = { Created: 'Cooking', Cooking: 'Done', Done: 'Served' }
const ACTIVE: OrderStatus[] = ['Created', 'Cooking', 'Done']
type View = 'active' | 'all'

/**
 * Заказы в работе — рядом с меню: каждый принятый заказ виден до выдачи, следующий шаг — одной кнопкой.
 * «Все» — история с выданными и отменёнными.
 */
/** Заказ, только что принятый с этого экрана. */
export interface PlacedOrder {
  id: string
  number: string
  total: number
}

export function RecentOrders({ placed }: { placed?: PlacedOrder | null }) {
  const { t } = useTranslation('orders')
  const f = useFormat()
  const [view, setView] = useState<View>('active')
  const [size, setSize] = useState(PAGE)
  // Активных немного, а API фильтрует по одному статусу — берём свежие заказы и делим на месте.
  const query = useOrders(view === 'active' ? MAX_PAGE : size)
  const [cancelling, setCancelling] = useState<OrderListItem | null>(null)
  const all = query.data?.items ?? []
  const items = view === 'active' ? all.filter((o) => ACTIVE.includes(o.status)) : all
  const activeCount = all.filter((o) => ACTIVE.includes(o.status)).length

  return (
    <Panel title={t('recent.title')} subtitle={t('recent.subtitle')} titleId="orders-recent">
      {/* Без всплывающего тоста: зрячий видит подсвеченный заказ, диктор — эту фразу. */}
      <p role="status" className="visually-hidden">
        {placed ? t('cart.done', { number: placed.number, total: f.money(placed.total) }) : ''}
      </p>
      <Segmented
        label={t('recent.view')}
        value={view}
        options={[
          { value: 'active', label: t('recent.active'), count: query.data ? activeCount : undefined },
          { value: 'all', label: t('recent.all') },
        ]}
        onChange={setView}
      />
      {query.isPending ? (
        <LoadingState lines={4} />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t(view === 'active' ? 'recent.emptyActive' : 'recent.empty')}>{t('recent.emptyHint')}</EmptyState>
      ) : (
        <ScrollArea label={t('recent.title')} size="half">
          <ul className={styles.list}>
            {items.map((order) => (
              <OrderRow key={order.id} order={order} fresh={order.id === placed?.id} onCancel={() => setCancelling(order)} />
            ))}
          </ul>
          {view === 'all' && query.data && query.data.total > items.length && size < MAX_PAGE && (
            <Button variant="text" className={styles.more} onClick={() => setSize(Math.min(MAX_PAGE, size + PAGE))}>
              {t('recent.more')}
            </Button>
          )}
        </ScrollArea>
      )}
      <CancelSheet order={cancelling} onClose={() => setCancelling(null)} />
    </Panel>
  )
}

function OrderRow({ order, fresh, onCancel }: { order: OrderListItem; fresh: boolean; onCancel: () => void }) {
  const row = useRef<HTMLLIElement>(null)
  // Новый заказ — в поле зрения списка (прокрутка внутри области заказов, без прыжка страницы).
  useEffect(() => {
    if (fresh) row.current?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }, [fresh])
  const { t } = useTranslation('orders')
  const f = useFormat()
  const errorText = useErrorText()
  const role = useSession().user?.role
  const canChange = role === 'Chef' || role === 'Manager'
  const status = useOrderStatus()
  const [open, setOpen] = useState(false)
  const detailsId = useId()
  const next = NEXT[order.status]
  const cancellable = order.status === 'Created' || order.status === 'Cooking'

  return (
    <li ref={row} className={`${styles.row} ${order.status === 'Done' ? styles.ready : ''} ${fresh ? styles.fresh : ''}`}>
      <button type="button" className={styles.toggle} aria-expanded={open} aria-controls={detailsId} onClick={() => setOpen(!open)}>
        <span className={styles.number}>{order.number}</span>
        <span className={styles.total}>{f.money(order.total)}</span>
        <span className={styles.status}>
          <StatusMark tone={STATUS_TONE[order.status]} weight="regular">
            {t(`status.${order.status}`)}
          </StatusMark>
        </span>
        <span className={styles.meta}>
          {f.moment(order.createdAt)} · {t('portions', { count: order.portions })}
        </span>
        <ChevronDown className={styles.chevron} aria-hidden="true" strokeWidth={1.8} />
      </button>
      {/* Принятый заказ уже забрал продукты со склада (списание по FEFO при приёме) — говорим это прямо. */}
      {order.status === 'Created' && <p className={styles.reserved}>{t('recent.reserved')}</p>}
      {canChange && (next || cancellable) && (
        <div className={styles.actions}>
          {next && (
            <Button
              variant={next === 'Served' ? 'primary' : 'secondary'}
              disabled={status.isPending}
              onClick={() => status.mutate({ id: order.id, status: next }, { onError: (cause) => toast.show(errorText(cause), 'crit') })}
            >
              {t(`next.${next}`)}
            </Button>
          )}
          {cancellable && (
            <Button variant="text" onClick={onCancel}>
              {t('cancel.action')}
            </Button>
          )}
        </div>
      )}
      <div id={detailsId} className={styles.details} hidden={!open}>
        {open && <OrderDetails id={order.id} />}
      </div>
    </li>
  )
}

function OrderDetails({ id }: { id: string }) {
  const { t } = useTranslation('orders')
  const f = useFormat()
  const query = useOrder(id)
  if (query.isPending) return <LoadingState lines={2} />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  const order = query.data

  return (
    <div className={styles.detailGrid}>
      <div>
        <h4 className={styles.detailTitle}>{t('details.items')}</h4>
        <ul className={styles.detailList}>
          {order.items.map((item) => (
            <li key={item.lineNo}>
              {t('details.item', { dish: item.dishName, count: item.quantity, amount: f.money(item.amount) })}
            </li>
          ))}
        </ul>
        {order.substitutions?.length > 0 && (
          <>
            <h4 className={styles.detailTitle}>{t('substitute.applied')}</h4>
            <ul className={styles.detailList}>
              {order.substitutions.map((s) => (
                <li key={s.productId}>{t('substitute.line', { product: s.productName, substitute: s.substituteName, ratio: f.number(s.ratio) })}</li>
              ))}
            </ul>
          </>
        )}
      </div>
      <div>
        <h4 className={styles.detailTitle}>{t('details.consumption')}</h4>
        <ul className={styles.detailList}>
          {order.consumption.map((line) => (
            <li key={`${line.batchId}`}>
              {t('details.consumed', { product: line.productName, qty: f.qty(line.consumed, line.unit), until: f.date(line.batchExpiresAt) })}
              {line.returned > 0 && ` · ${t('details.returned', { qty: f.qty(line.returned, line.unit) })}`}
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}

function CancelSheet({ order, onClose }: { order: OrderListItem | null; onClose: () => void }) {
  const { t } = useTranslation('orders')
  const errorText = useErrorText()
  const mutation = useCancelOrder()
  return (
    <Sheet
      open={order !== null}
      onClose={onClose}
      title={t('cancel.title', { number: order?.number ?? '' })}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('cancel.keep')}
          </Button>
          <Button
            disabled={mutation.isPending}
            onClick={() =>
              order &&
              mutation.mutate(order.id, {
                onSuccess: (result) => {
                  // Отмену человек только что сделал сам — сообщаем только неочевидное: часть продуктов не вернулась.
                  const lost = (result as { lost?: unknown[] }).lost?.length ?? 0
                  if (lost) toast.show(t('cancel.doneLost'), 'warn')
                  onClose()
                },
                onError: (cause) => toast.show(errorText(cause), 'crit'),
              })
            }
          >
            {t('cancel.confirm')}
          </Button>
        </>
      }
    >
      <p className={styles.cancelText}>{t('cancel.text')}</p>
    </Sheet>
  )
}

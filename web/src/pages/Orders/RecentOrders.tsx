import { ChevronDown } from 'lucide-react'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useCancelOrder, useOrderStatus } from '@/api/mutations'
import { useOrder, useOrders } from '@/api/queries'
import type { OrderListItem, OrderStatus } from '@/api/types'
import { useSession } from '@/auth/session'
import { Button } from '@/components/Button'
import { Panel } from '@/components/Panel'
import { Sheet } from '@/components/Sheet'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import type { Tone } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import styles from './RecentOrders.module.scss'

const PAGE = 20
const MAX_PAGE = 100

const STATUS_TONE: Record<OrderStatus, Tone> = { Created: 'info', Cooking: 'warn', Done: 'fresh', Cancelled: 'expired' }
/** Следующий шаг заказа: Принят → Готовится → Готов. */
const NEXT: Partial<Record<OrderStatus, OrderStatus>> = { Created: 'Cooking', Cooking: 'Done' }

/** Последние заказы: статус словами, следующий шаг одной кнопкой, отмена с подтверждением. */
export function RecentOrders() {
  const { t } = useTranslation('orders')
  const [size, setSize] = useState(PAGE)
  const query = useOrders(size)
  const [cancelling, setCancelling] = useState<OrderListItem | null>(null)
  const items = query.data?.items ?? []

  return (
    <Panel title={t('recent.title')} subtitle={t('recent.subtitle')} titleId="orders-recent">
      {query.isPending ? (
        <LoadingState lines={4} />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t('recent.empty')}>{t('recent.emptyHint')}</EmptyState>
      ) : (
        <>
          <ul className={styles.list}>
            {items.map((order) => (
              <OrderRow key={order.id} order={order} onCancel={() => setCancelling(order)} />
            ))}
          </ul>
          {query.data && query.data.total > items.length && size < MAX_PAGE && (
            <Button variant="text" className={styles.more} onClick={() => setSize(Math.min(MAX_PAGE, size + PAGE))}>
              {t('recent.more')}
            </Button>
          )}
        </>
      )}
      <CancelSheet order={cancelling} onClose={() => setCancelling(null)} />
    </Panel>
  )
}

function OrderRow({ order, onCancel }: { order: OrderListItem; onCancel: () => void }) {
  const { t } = useTranslation('orders')
  const f = useFormat()
  const errorText = useErrorText()
  const role = useSession().user?.role
  const canChange = role === 'Chef' || role === 'Manager'
  const status = useOrderStatus()
  const [open, setOpen] = useState(false)
  const detailsId = useId()
  const next = NEXT[order.status]

  return (
    <li className={styles.row}>
      <div className={styles.head}>
        <button type="button" className={styles.toggle} aria-expanded={open} aria-controls={detailsId} onClick={() => setOpen(!open)}>
          <span className={styles.number}>{order.number}</span>
          <span className={styles.when}>{f.moment(order.createdAt)}</span>
          <span className={styles.portions}>{t('portions', { count: order.portions })}</span>
          <span className={styles.total}>{f.money(order.total)}</span>
          <span className={styles.status}>
            <StatusMark tone={STATUS_TONE[order.status]} weight="regular">
              {t(`status.${order.status}`)}
            </StatusMark>
          </span>
          <ChevronDown className={styles.chevron} aria-hidden="true" strokeWidth={1.8} />
        </button>
        {canChange && (next || order.status === 'Created' || order.status === 'Cooking') && (
          <div className={styles.actions}>
            {next && (
              <Button
                variant="secondary"
                disabled={status.isPending}
                onClick={() =>
                  status.mutate(
                    { id: order.id, status: next },
                    { onError: (cause) => toast.show(errorText(cause), 'crit') },
                  )
                }
              >
                {t(`next.${next}`)}
              </Button>
            )}
            <Button variant="text" onClick={onCancel}>
              {t('cancel.action')}
            </Button>
          </div>
        )}
      </div>
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
                  const lost = (result as { lost?: unknown[] }).lost?.length ?? 0
                  toast.show(lost ? t('cancel.doneLost') : t('cancel.done'), lost ? 'warn' : 'fresh')
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

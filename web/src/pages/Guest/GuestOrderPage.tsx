import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { ApiError } from '@/api/errors'
import { useGuestOrder } from '@/api/guest'
import type { OrderStatus } from '@/api/types'
import { LinkButton } from '@/components/Button'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { useFormat } from '@/lib/useFormat'
import { GuestShell } from './GuestShell'
import styles from './GuestOrderPage.module.scss'

const STEPS: OrderStatus[] = ['Created', 'Cooking', 'Done', 'Served']

/** Статус заказа гостя: номер крупно, шаги «Принят → Готовится → Готов → Выдан»; обновляется сам раз в 5 с. */
export function GuestOrderPage() {
  const { key = '', token = '' } = useParams()
  const { t } = useTranslation(['guest', 'orders'])
  const f = useFormat()
  const order = useGuestOrder(key, token)
  const back = (
    <LinkButton variant="secondary" to={`/m/${key}`}>
      {t('order.back')}
    </LinkButton>
  )

  if (order.isPending)
    return (
      <GuestShell>
        <LoadingState lines={5} />
      </GuestShell>
    )
  if (order.error)
    return (
      <GuestShell>
        {order.error instanceof ApiError && order.error.status === 404 ? (
          <EmptyState title={t('order.notFound.title')} action={back}>
            {t('order.notFound.text')}
          </EmptyState>
        ) : (
          <ErrorState error={order.error} onRetry={() => void order.refetch()} />
        )}
      </GuestShell>
    )

  const data = order.data
  const current = STEPS.indexOf(data.status)
  return (
    <GuestShell table={data.table}>
      <section className={styles.order} aria-labelledby="order-title">
        <h1 id="order-title" className={styles.number}>
          {t('order.title', { number: data.number })}
        </h1>
        <p className={styles.subtitle}>{t('order.subtitle')}</p>

        {data.status === 'Cancelled' ? (
          <p className={styles.cancelled} role="status">
            <strong>{t('order.status.Cancelled')}.</strong> {t('order.hint.Cancelled')}
          </p>
        ) : (
          <>
            <ol className={styles.steps} aria-label={t('order.progress')}>
              {STEPS.map((step, index) => (
                <li key={step} className={styles.step} data-state={index < current ? 'done' : index === current ? 'current' : 'next'} aria-current={index === current ? 'step' : undefined}>
                  <span className={styles.marker} aria-hidden="true" />
                  {t(`order.status.${step}`)}
                </li>
              ))}
            </ol>
            <p className={styles.hint} role="status" aria-live="polite">
              {t(`order.hint.${data.status}`)}
            </p>
          </>
        )}

        <h2 className={styles.itemsTitle}>{t('order.items')}</h2>
        <ul className={styles.items}>
          {data.items.map((item, index) => (
            <li key={index} className={styles.item}>
              <span>
                {item.quantity} × {item.dishName}
                {item.modifications.length > 0 && (
                  <span className={styles.changes}>
                    {item.modifications
                      .map((m) => (m.kind === 'Remove' ? t('orders:change.remove', { product: m.productName }) : t('orders:change.replace', { product: m.productName, substitute: m.substituteName })))
                      .join(' · ')}
                  </span>
                )}
              </span>
              <span className={styles.amount}>{f.money(item.amount)}</span>
            </li>
          ))}
        </ul>
        <p className={styles.total}>
          {t('order.total')} <strong>{f.money(data.total)}</strong>
        </p>
        <p className={styles.payment}>{t('order.paymentLabel', { method: t(`payment.${data.payment}`) })}</p>
        {back}
      </section>
    </GuestShell>
  )
}

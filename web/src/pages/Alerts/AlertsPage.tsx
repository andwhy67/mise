import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAckAlert } from '@/api/mutations'
import { useAlertSummary, useAlerts, type AlertFilter } from '@/api/queries'
import type { Alert } from '@/api/types'
import { Button, LinkButton } from '@/components/Button'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { ScrollArea } from '@/components/ScrollArea'
import { Segmented } from '@/components/Segmented'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import { alertText, alertTone } from './alertText'
import styles from './AlertsPage.module.scss'

const PAGE = 30
const MAX_PAGE = 100

/** Уведомления: что случилось на складе и в меню; «Понятно» — подтверждение, что разобрались. */
export function AlertsPage() {
  const { t } = useTranslation('alerts')
  const errorText = useErrorText()
  const [filter, setFilter] = useState<AlertFilter>('attention')
  const [size, setSize] = useState(PAGE)
  const query = useAlerts(filter, size)
  const summary = useAlertSummary()
  const ack = useAckAlert()
  const [ackingAll, setAckingAll] = useState(false)
  const items = query.data?.items ?? []
  const unacknowledged = items.filter((a) => !a.acknowledgedAt)

  // Отдельного «отметить всё» в API нет: подтверждаем по одному (операция идемпотентна).
  const ackAll = async () => {
    setAckingAll(true)
    try {
      for (const alert of unacknowledged) await ack.mutateAsync(alert.id)
      toast.show(t('ackAllDone', { count: unacknowledged.length }))
    } catch (cause) {
      toast.show(errorText(cause), 'crit')
    } finally {
      setAckingAll(false)
    }
  }

  return (
    <Page
      title={t('title')}
      subtitle={t('subtitle')}
      actions={
        filter === 'attention' &&
        unacknowledged.length > 1 && (
          <Button variant="secondary" disabled={ackingAll} onClick={() => void ackAll()}>
            {t('ackAll')}
          </Button>
        )
      }
    >
      <Segmented
        label={t('filter.label')}
        value={filter}
        options={[
          { value: 'attention', label: t('filter.attention'), count: summary.data?.total },
          { value: 'all', label: t('filter.all') },
        ]}
        onChange={(value) => {
          setFilter(value)
          setSize(PAGE)
        }}
      />
      <Panel>
        {query.isPending ? (
          <LoadingState lines={5} />
        ) : query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : items.length === 0 ? (
          <EmptyState title={t(`empty.${filter}`)}>{t(`empty.${filter}Hint`)}</EmptyState>
        ) : (
          <>
            <ScrollArea label={t('title')}>
              <ul className={styles.list}>
                {items.map((alert) => (
                  <AlertRow key={alert.id} alert={alert} />
                ))}
              </ul>
            </ScrollArea>
            {query.data && query.data.total > items.length && size < MAX_PAGE && (
              <Button variant="text" className={styles.more} onClick={() => setSize(Math.min(MAX_PAGE, size + PAGE))}>
                {t('more')}
              </Button>
            )}
          </>
        )}
      </Panel>
    </Page>
  )
}

function AlertRow({ alert }: { alert: Alert }) {
  const { t } = useTranslation('alerts')
  const f = useFormat()
  const errorText = useErrorText()
  const ack = useAckAlert()
  const link = alert.dishId ? { to: '/stoplist', label: t('open.stoplist') } : alert.productId ? { to: '/stock', label: t('open.stock') } : null

  return (
    <li className={`${styles.row} ${alert.acknowledgedAt ? styles.done : ''}`}>
      <div className={styles.body}>
        <StatusMark tone={alertTone(alert)}>{alertText(alert, f, t)}</StatusMark>
        <p className={styles.meta}>
          {f.moment(alert.createdAt)}
          {alert.acknowledgedAt && ` · ${t('acknowledged', { when: f.moment(alert.acknowledgedAt) })}`}
          {!alert.isActive && !alert.acknowledgedAt && ` · ${t('resolved')}`}
        </p>
      </div>
      <div className={styles.actions}>
        {link && (
          <LinkButton to={link.to} variant="text">
            {link.label}
          </LinkButton>
        )}
        {!alert.acknowledgedAt && (
          <Button
            variant="secondary"
            disabled={ack.isPending}
            onClick={() => ack.mutate(alert.id, { onError: (cause) => toast.show(errorText(cause), 'crit') })}
          >
            {t('ack')}
          </Button>
        )}
      </div>
    </li>
  )
}

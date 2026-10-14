import { useQuery } from '@tanstack/react-query'
import { ChevronDown } from 'lucide-react'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '@/api/client'
import { useUsers } from '@/api/queries'
import type { Paged } from '@/api/types'
import { Button } from '@/components/Button'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { ScrollArea } from '@/components/ScrollArea'
import { Select } from '@/components/Select'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { useFormat } from '@/lib/useFormat'
import styles from './AuditPage.module.scss'

interface AuditEntry {
  id: string
  createdAt: string
  action: string
  userId: string | null
  login: string | null
  entity: string | null
  entityId: string | null
  details: Record<string, unknown> | null
  ip: string | null
}

/** Действия журнала — как их пишет сервер (AuditFilter и AuthService). Группы — для фильтра. */
const ACTION_GROUPS: Record<string, string[]> = {
  stock: ['supply.create', 'batch.write_off', 'demo.seed_stock'],
  orders: ['order.create', 'order.status', 'order.cancel'],
  catalog: ['product.create', 'product.update', 'product.delete', 'dish.create', 'dish.update', 'dish.ingredients', 'dish.delete'],
  people: ['auth.login', 'auth.demo_login', 'auth.login_failed', 'auth.logout', 'auth.refresh_reuse', 'user.create', 'user.update', 'user.reset_password'],
  simulator: ['simulator.start', 'simulator.stop', 'simulator.scenario', 'simulator.speed'],
}

const PAGE = 50
const MAX_PAGE = 100

/** Журнал (менеджер): кто, что и когда сделал. Технические детали — по раскрытию. */
export function AuditPage() {
  const { t } = useTranslation('audit')
  const users = useUsers().data ?? []
  const [action, setAction] = useState('')
  const [userId, setUserId] = useState('')
  const [size, setSize] = useState(PAGE)
  const params = new URLSearchParams({ pageSize: String(size) })
  if (action) params.set('action', action)
  if (userId) params.set('userId', userId)
  const query = useQuery({
    queryKey: ['audit', action, userId, size],
    queryFn: ({ signal }) => api.get<Paged<AuditEntry>>(`/api/v1/audit?${params}`, { signal }),
    placeholderData: (previous) => previous,
  })
  const items = query.data?.items ?? []

  return (
    <Page title={t('title')} subtitle={t('subtitle')}>
      <div className={styles.filters}>
        <Select
          label={t('filter.action')}
          value={action}
          onChange={(e) => setAction(e.target.value)}
          options={[{ value: '', label: t('filter.allActions') }]}
          groups={Object.entries(ACTION_GROUPS).map(([group, actions]) => ({
            label: t(`groups.${group}`),
            options: actions.map((a) => ({ value: a, label: t(`actions.${a}`, { defaultValue: a }) })),
          }))}
        />
        <Select
          label={t('filter.user')}
          value={userId}
          onChange={(e) => setUserId(e.target.value)}
          options={[{ value: '', label: t('filter.allUsers') }, ...users.map((u) => ({ value: u.id, label: `${u.displayName} (${u.login})` }))]}
        />
      </div>
      <Panel>
        {query.isPending ? (
          <LoadingState lines={6} />
        ) : query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : items.length === 0 ? (
          <EmptyState title={t('empty')}>{t('emptyHint')}</EmptyState>
        ) : (
          <>
            <ScrollArea label={t('title')}>
              <ul className={styles.list}>
                {items.map((entry) => (
                  <AuditRow key={entry.id} entry={entry} />
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

function AuditRow({ entry }: { entry: AuditEntry }) {
  const { t } = useTranslation('audit')
  const f = useFormat()
  const [open, setOpen] = useState(false)
  const detailsId = useId()
  const failed = entry.action.endsWith('_failed') || entry.action === 'auth.refresh_reuse' || Number(entry.details?.status) >= 400

  return (
    <li className={styles.row}>
      <button type="button" className={styles.toggle} aria-expanded={open} aria-controls={detailsId} onClick={() => setOpen(!open)}>
        <span className={styles.when}>{f.moment(entry.createdAt)}</span>
        {/* Выход идёт по refresh-cookie без токена — сервер не знает, кто это; «Система» здесь была бы неправдой. */}
        <span className={styles.who}>{entry.login ?? (entry.action.startsWith('auth.') ? '—' : t('system'))}</span>
        <span className={`${styles.what} ${failed ? styles.failed : ''}`}>{t(`actions.${entry.action}`, { defaultValue: entry.action })}</span>
        <ChevronDown className={styles.chevron} aria-hidden="true" strokeWidth={1.8} />
      </button>
      <div id={detailsId} className={styles.details} hidden={!open}>
        {open && (
          // Журнал — для разбора: здесь технические поля уместны (DESIGN.md §1.1, п. 7).
          <dl className={styles.fields}>
            {entry.entity && (
              <>
                <dt>{t('fields.entity')}</dt>
                <dd>
                  {entry.entity} · <code>{entry.entityId}</code>
                </dd>
              </>
            )}
            {entry.ip && (
              <>
                <dt>{t('fields.ip')}</dt>
                <dd>
                  <code>{entry.ip}</code>
                </dd>
              </>
            )}
            {Object.entries(entry.details ?? {})
              .filter(([, value]) => value !== null && value !== '')
              .map(([key, value]) => (
                <div key={key} className={styles.field}>
                  <dt>{key}</dt>
                  <dd>
                    <code>{typeof value === 'object' ? JSON.stringify(value) : String(value)}</code>
                  </dd>
                </div>
              ))}
          </dl>
        )}
      </div>
    </li>
  )
}

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy, Link as LinkIcon, UserPlus } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '@/api/client'
import type { UserRole } from '@/auth/session'
import { Button } from '@/components/Button'
import { Panel } from '@/components/Panel'
import { Select } from '@/components/Select'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import type { Tone } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import styles from './UsersPage.module.scss'

type InviteStatus = 'Active' | 'Accepted' | 'Revoked' | 'Expired'

interface Invite {
  id: string
  /** «K7Q2-PXM4». */
  code: string
  role: Exclude<UserRole, 'Simulator'>
  status: InviteStatus
  createdAt: string
  expiresAt: string
  acceptedAt: string | null
  acceptedBy: string | null
}

const INVITES_KEY = ['invites'] as const
const ROLES: Invite['role'][] = ['Chef', 'Warehouse', 'Manager']
const STATUS_TONE: Record<InviteStatus, Tone> = { Active: 'fresh', Accepted: 'info', Revoked: 'muted', Expired: 'muted' }

/** Ссылка, которую менеджер отправляет сотруднику: открывается экран «По приглашению» с уже введённым кодом. */
const inviteLink = (code: string) => `${window.location.origin}/join/${code}`

async function copy(text: string) {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return false
  }
}

/**
 * Приглашения (менеджер): сотрудник регистрируется сам по коду и попадает в этот ресторан с выбранной ролью.
 * Код одноразовый и живёт 7 дней; активный можно отозвать.
 */
export function InvitesPanel() {
  const { t } = useTranslation('users')
  const f = useFormat()
  const errorText = useErrorText()
  const client = useQueryClient()
  const [role, setRole] = useState<Invite['role']>('Chef')
  const query = useQuery({ queryKey: INVITES_KEY, queryFn: ({ signal }) => api.get<Invite[]>('/api/v1/invites', { signal }) })
  const create = useMutation({
    mutationFn: (body: { role: Invite['role'] }) => api.post<Invite>('/api/v1/invites', body),
    onSuccess: () => void client.invalidateQueries({ queryKey: INVITES_KEY }),
  })
  const revoke = useMutation({
    mutationFn: (id: string) => api.delete<Invite>(`/api/v1/invites/${id}`),
    onSuccess: () => void client.invalidateQueries({ queryKey: INVITES_KEY }),
  })

  const share = async (invite: Invite, what: 'code' | 'link') => {
    const ok = await copy(what === 'code' ? invite.code : inviteLink(invite.code))
    toast.show(ok ? t(what === 'code' ? 'invites.codeCopied' : 'invites.linkCopied', { code: invite.code }) : t('invites.copyFailed'), ok ? undefined : 'crit')
  }

  const invites = query.data ?? []
  const active = invites.filter((i) => i.status === 'Active')
  const history = invites.filter((i) => i.status !== 'Active')

  return (
    <Panel title={t('invites.title')} subtitle={t('invites.subtitle')} titleId="users-invites">
      <div className={styles.inviteCreate}>
        <Select
          label={t('invites.role')}
          options={ROLES.map((r) => ({ value: r, label: t(`roles.${r}`) }))}
          value={role}
          onChange={(e) => setRole(e.target.value as Invite['role'])}
        />
        <Button
          icon={<UserPlus />}
          disabled={create.isPending}
          onClick={() =>
            create.mutate(
              { role },
              {
                onSuccess: (invite) => void share(invite, 'link'),
                onError: (cause) => toast.show(errorText(cause), 'crit'),
              },
            )
          }
        >
          {t('invites.create')}
        </Button>
      </div>

      {query.isPending ? (
        <LoadingState lines={2} />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : invites.length === 0 ? (
        <EmptyState title={t('invites.empty')}>{t('invites.emptyHint')}</EmptyState>
      ) : (
        <ul className={styles.list}>
          {[...active, ...history].map((invite) => (
            <li key={invite.id} className={styles.invite}>
              <span className={styles.name}>
                <span className={styles.inviteCode}>{invite.code}</span>
                <span className={styles.meta}>
                  {t(`roles.${invite.role}`)} ·{' '}
                  {invite.status === 'Active'
                    ? t('invites.until', { when: f.moment(invite.expiresAt) })
                    : invite.status === 'Accepted'
                      ? t('invites.acceptedBy', { name: invite.acceptedBy ?? '—', when: f.moment(invite.acceptedAt!) })
                      : t(`invites.status.${invite.status}`)}
                </span>
              </span>
              {invite.status === 'Active' ? (
                <span className={styles.inviteActions}>
                  <Button variant="text" icon={<LinkIcon />} onClick={() => void share(invite, 'link')} aria-label={t('invites.copyLinkLabel', { code: invite.code })}>
                    {t('invites.copyLink')}
                  </Button>
                  <Button variant="text" icon={<Copy />} onClick={() => void share(invite, 'code')} aria-label={t('invites.copyCodeLabel', { code: invite.code })}>
                    {t('invites.copyCode')}
                  </Button>
                  <Button
                    variant="text"
                    disabled={revoke.isPending}
                    onClick={() => revoke.mutate(invite.id, { onError: (cause) => toast.show(errorText(cause), 'crit') })}
                    aria-label={t('invites.revokeLabel', { code: invite.code })}
                  >
                    {t('invites.revoke')}
                  </Button>
                </span>
              ) : (
                <StatusMark tone={STATUS_TONE[invite.status]} weight="regular">
                  {t(`invites.status.${invite.status}`)}
                </StatusMark>
              )}
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

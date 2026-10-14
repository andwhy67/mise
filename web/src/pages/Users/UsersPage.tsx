import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Plus } from 'lucide-react'
import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '@/api/client'
import { ApiError } from '@/api/errors'
import { useUsers } from '@/api/queries'
import type { User } from '@/api/types'
import { useSession, type UserRole } from '@/auth/session'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { Select } from '@/components/Select'
import { Sheet } from '@/components/Sheet'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import styles from './UsersPage.module.scss'


const ROLES: UserRole[] = ['Manager', 'Chef', 'Warehouse', 'Simulator']
/** Как на сервере: пароль — не короче 8 символов (assumptions H3). */
const MIN_PASSWORD = 8
const USERS_KEY = ['users'] as const

/** Пользователи (менеджер): учётные записи, роли, отключение и сброс пароля. */
export function UsersPage() {
  const { t } = useTranslation('users')
  const f = useFormat()
  const query = useUsers()
  const me = useSession().user
  const [editing, setEditing] = useState<{ user: User | null } | null>(null)
  const users = [...(query.data ?? [])].sort((a, b) => Number(b.isActive) - Number(a.isActive) || ROLES.indexOf(a.role) - ROLES.indexOf(b.role))

  return (
    <Page
      title={t('title')}
      subtitle={t('subtitle')}
      actions={
        <Button icon={<Plus />} onClick={() => setEditing({ user: null })}>
          {t('add')}
        </Button>
      }
    >
      <Panel>
        {query.isPending ? (
          <LoadingState lines={5} />
        ) : query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : users.length === 0 ? (
          <EmptyState title={t('empty')} />
        ) : (
          <ul className={styles.list}>
            {users.map((user) => (
              <li key={user.id}>
                <button type="button" className={styles.row} onClick={() => setEditing({ user })}>
                  <span className={styles.name}>
                    <span className={styles.title}>
                      {user.displayName}
                      {user.id === me?.id && <span className={styles.you}>{t('you')}</span>}
                    </span>
                    <span className={styles.meta}>{user.login}</span>
                  </span>
                  <span className={styles.cell}>{t(`roles.${user.role}`)}</span>
                  <span className={styles.cell}>
                    {user.isActive ? (
                      user.lastLoginAt ? (
                        t('lastLogin', { when: f.moment(user.lastLoginAt) })
                      ) : (
                        t('neverLoggedIn')
                      )
                    ) : (
                      <StatusMark tone="expired" weight="regular">
                        {t('inactive')}
                      </StatusMark>
                    )}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </Panel>
      <UserSheet key={editing?.user?.id ?? (editing ? 'new' : 'closed')} open={editing !== null} user={editing?.user ?? null} onClose={() => setEditing(null)} />
    </Page>
  )
}

function useSaveUser() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: unknown }) => (id ? api.put(`/api/v1/users/${id}`, body) : api.post('/api/v1/users', body)),
    onSuccess: () => client.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

function UserSheet({ open, user, onClose }: { open: boolean; user: User | null; onClose: () => void }) {
  const { t } = useTranslation('users')
  return (
    <Sheet open={open} onClose={onClose} title={user ? user.displayName : t('new')} subtitle={user ? user.login : t('newHint')}>
      {open && (
        <>
          <UserForm user={user} onDone={onClose} />
          {user && <PasswordReset user={user} />}
        </>
      )}
    </Sheet>
  )
}

function UserForm({ user, onDone }: { user: User | null; onDone: () => void }) {
  const { t } = useTranslation('users')
  const errorText = useErrorText()
  const save = useSaveUser()
  const me = useSession().user
  const activeId = useId()
  const [login, setLogin] = useState(user?.login ?? '')
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [role, setRole] = useState<UserRole>(user?.role ?? 'Chef')
  const [isActive, setIsActive] = useState(user?.isActive ?? true)
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [serverError, setServerError] = useState<string | null>(null)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setServerError(null)
    const found: Record<string, string> = {}
    if (!user && !/^[a-z0-9._-]{3,32}$/i.test(login.trim())) found.login = t('errors.login')
    if (!displayName.trim()) found.displayName = t('errors.displayName')
    if (!user && password.length < MIN_PASSWORD) found.password = t('errors.password', { min: MIN_PASSWORD })
    setErrors(found)
    if (Object.keys(found).length) return

    const body = user ? { displayName: displayName.trim(), role, isActive } : { login: login.trim(), displayName: displayName.trim(), role, password }
    save.mutate(
      { id: user?.id, body },
      {
        onSuccess: () => {
          toast.show(t(user ? 'saved' : 'created', { name: displayName.trim() }))
          onDone()
        },
        onError: (cause) => {
          if (cause instanceof ApiError && cause.code === 'user.duplicate_login') setErrors({ login: t('errors.duplicate') })
          else setServerError(errorText(cause))
        },
      },
    )
  }

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      {!user && <Field label={t('login')} value={login} autoComplete="off" maxLength={32} onChange={(e) => setLogin(e.target.value)} error={errors.login} hint={t('loginHint')} />}
      <Field label={t('displayName')} value={displayName} maxLength={200} onChange={(e) => setDisplayName(e.target.value)} error={errors.displayName} />
      <Select label={t('role')} options={ROLES.map((r) => ({ value: r, label: t(`roles.${r}`) }))} value={role} onChange={(e) => setRole(e.target.value as UserRole)} />
      <p className={styles.note}>{t(`roleHints.${role}`)}</p>
      {!user && (
        <Field
          label={t('password')}
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          error={errors.password}
          hint={t('passwordHint', { min: MIN_PASSWORD })}
        />
      )}
      {user && (
        <label className={styles.checkbox} htmlFor={activeId}>
          <input id={activeId} type="checkbox" checked={isActive} disabled={user.id === me?.id} onChange={(e) => setIsActive(e.target.checked)} />
          <span>
            {t('active')}
            <span className={styles.hint}>{user.id === me?.id ? t('activeSelf') : t('activeHint')}</span>
          </span>
        </label>
      )}
      {serverError && (
        <p className={styles.error} role="alert">
          {serverError}
        </p>
      )}
      <div className={styles.actions}>
        <Button variant="secondary" onClick={onDone}>
          {t('cancel')}
        </Button>
        <Button type="submit" disabled={save.isPending}>
          {t(user ? 'save' : 'create')}
        </Button>
      </div>
    </form>
  )
}

function PasswordReset({ user }: { user: User }) {
  const { t } = useTranslation('users')
  const errorText = useErrorText()
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | undefined>()
  const reset = useMutation({ mutationFn: (value: string) => api.post(`/api/v1/users/${user.id}/password`, { password: value }) })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (password.length < MIN_PASSWORD) return setError(t('errors.password', { min: MIN_PASSWORD }))
    setError(undefined)
    reset.mutate(password, {
      onSuccess: () => {
        setPassword('')
        toast.show(t('passwordReset', { name: user.displayName }))
      },
      onError: (cause) => setError(errorText(cause)),
    })
  }

  return (
    <form className={styles.reset} onSubmit={submit} noValidate>
      <h3 className={styles.resetTitle}>{t('resetTitle')}</h3>
      <p className={styles.note}>{t('resetHint')}</p>
      <div className={styles.resetRow}>
        <Field label={t('newPassword')} type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} error={error} />
        <Button type="submit" variant="secondary" className={styles.resetButton} disabled={reset.isPending}>
          {t('reset')}
        </Button>
      </div>
    </form>
  )
}

import { useQuery } from '@tanstack/react-query'
import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '@/api/client'
import { ApiError } from '@/api/errors'
import { session, type UserRole } from '@/auth/session'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { useErrorText } from '@/lib/useErrorText'
import {
  PASSWORD_MIN,
  emptyJoin,
  emptyRegister,
  formatCode,
  isCompleteCode,
  joinRequest,
  normalizeCode,
  registerRequest,
  validateJoin,
  validateRegister,
  type JoinForm,
  type RegisterForm,
} from './accountForm'
import styles from './LoginPage.module.scss'

/** Ответ сервера → поле формы: занятый логин — у логина, неизвестный код — у кода; остальное — общей строкой. */
function serverFieldError(cause: unknown): { field: string; code: string } | null {
  if (!(cause instanceof ApiError)) return null
  if (cause.code === 'user.duplicate_login') return { field: 'login', code: 'loginTaken' }
  if (cause.status === 404) return { field: 'code', code: 'codeNotFound' }
  return null
}

function useSubmit(onDone: () => void) {
  const errorText = useErrorText()
  const [busy, setBusy] = useState(false)
  const [general, setGeneral] = useState<string | null>(null)
  const run = async (action: () => Promise<void>, setField: (field: string, code: string) => void) => {
    setBusy(true)
    setGeneral(null)
    try {
      await action()
      onDone()
    } catch (cause) {
      const field = serverFieldError(cause)
      if (field) setField(field.field, field.code)
      else setGeneral(errorText(cause))
    } finally {
      setBusy(false)
    }
  }
  return { busy, general, run }
}

/** Общие поля аккаунта: имя, логин, пароль. */
function AccountFields<T extends { displayName: string; login: string; password: string }>({
  form,
  errors,
  onChange,
}: {
  form: T
  errors: Record<string, string>
  onChange: (patch: Partial<T>) => void
}) {
  const { t } = useTranslation('auth')
  const err = (key: string) => (errors[key] ? t(`account.errors.${errors[key]}`) : undefined)
  return (
    <>
      <Field
        label={t('account.displayName')}
        name="name"
        autoComplete="name"
        value={form.displayName}
        maxLength={100}
        error={err('displayName')}
        onChange={(e) => onChange({ displayName: e.target.value } as Partial<T>)}
      />
      <Field
        label={t('login')}
        name="username"
        autoComplete="username"
        autoCapitalize="none"
        spellCheck={false}
        value={form.login}
        maxLength={32}
        hint={t('account.loginHint')}
        error={err('login')}
        onChange={(e) => onChange({ login: e.target.value } as Partial<T>)}
      />
      <Field
        label={t('password')}
        name="new-password"
        type="password"
        autoComplete="new-password"
        value={form.password}
        hint={t('account.passwordHint', { count: PASSWORD_MIN })}
        error={err('password')}
        onChange={(e) => onChange({ password: e.target.value } as Partial<T>)}
      />
    </>
  )
}

/** «Создать ресторан»: регистрирующийся становится менеджером своего, отдельного ресторана. */
export function RegisterForm({ onDone }: { onDone: () => void }) {
  const { t } = useTranslation('auth')
  const [form, setForm] = useState<RegisterForm>(emptyRegister)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const { busy, general, run } = useSubmit(onDone)
  const demoId = useId()
  const update = (patch: Partial<RegisterForm>) => setForm((current) => ({ ...current, ...patch }))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const found = validateRegister(form)
    setErrors(found)
    if (Object.keys(found).length) return
    void run(
      () => session.register(registerRequest(form)),
      (field, code) => setErrors({ [field]: code }),
    )
  }

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      <Field
        label={t('account.restaurantName')}
        name="organization"
        autoComplete="organization"
        value={form.restaurantName}
        maxLength={120}
        hint={t('account.restaurantHint')}
        error={errors.restaurantName ? t(`account.errors.${errors.restaurantName}`) : undefined}
        onChange={(e) => update({ restaurantName: e.target.value })}
      />
      <AccountFields form={form} errors={errors} onChange={update} />
      <label className={styles.checkbox} htmlFor={demoId}>
        <input id={demoId} type="checkbox" checked={form.fillDemoCatalog} onChange={(e) => update({ fillDemoCatalog: e.target.checked })} />
        <span>
          {t('account.fillDemoCatalog')}
          <span className={styles.checkboxHint}>{t('account.fillDemoCatalogHint')}</span>
        </span>
      </label>
      <p className={styles.error} aria-live="polite">
        {general}
      </p>
      <Button type="submit" size="lg" block disabled={busy}>
        {busy ? t('account.creating') : t('account.create')}
      </Button>
    </form>
  )
}

interface InvitePreview {
  restaurantName: string
  role: Exclude<UserRole, 'Simulator'>
  expiresAt: string
}

/** «По приглашению»: код из сообщения менеджера; до регистрации видно, куда и кем человек попадёт. */
export function JoinForm({ initialCode, onDone }: { initialCode?: string; onDone: () => void }) {
  const { t } = useTranslation('auth')
  const { t: tc } = useTranslation('common')
  const [form, setForm] = useState<JoinForm>(() => emptyJoin(formatCode(initialCode ?? '')))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const { busy, general, run } = useSubmit(onDone)
  const update = (patch: Partial<JoinForm>) => setForm((current) => ({ ...current, ...patch }))
  const code = normalizeCode(form.code)
  const complete = isCompleteCode(form.code)
  const preview = useQuery({
    queryKey: ['auth', 'invite', code],
    queryFn: ({ signal }) => api.get<InvitePreview>(`/api/v1/auth/invites/${code}`, { signal, anonymous: true }),
    enabled: complete,
    retry: false,
    staleTime: 60_000,
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const found = validateJoin(form)
    setErrors(found)
    if (Object.keys(found).length) return
    void run(
      () => session.join(joinRequest(form)),
      (field, codeKey) => setErrors({ [field]: codeKey }),
    )
  }

  const codeError = errors.code ? t(`account.errors.${errors.code}`) : complete && preview.isError ? t('account.errors.codeNotFound') : undefined

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      <Field
        label={t('account.code')}
        name="invite-code"
        autoComplete="one-time-code"
        autoCapitalize="characters"
        spellCheck={false}
        value={form.code}
        placeholder="K7Q2-PXM4"
        error={codeError}
        onChange={(e) => {
          update({ code: formatCode(e.target.value) })
          if (errors.code)
            setErrors((current) => {
              const next = { ...current }
              delete next.code
              return next
            })
        }}
      />
      {preview.data && (
        <p className={styles.invitePreview} aria-live="polite">
          {t('account.invitePreview', { restaurant: preview.data.restaurantName, role: tc(`roles.${preview.data.role}`).toLocaleLowerCase() })}
        </p>
      )}
      <AccountFields form={form} errors={errors} onChange={update} />
      <p className={styles.error} aria-live="polite">
        {general}
      </p>
      <Button type="submit" size="lg" block disabled={busy}>
        {busy ? t('account.joining') : t('account.join')}
      </Button>
    </form>
  )
}

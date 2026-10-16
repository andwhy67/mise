import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useWriteOff } from '@/api/mutations'
import type { Batch } from '@/api/types'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { Sheet } from '@/components/Sheet'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import { useOnline } from '@/pwa/network'
import { INPUT_UNITS } from './inputUnits'
import styles from './WriteOffSheet.module.scss'

const REASONS = ['spoiled', 'damaged', 'inventory', 'other'] as const
type Reason = (typeof REASONS)[number]

/** Ручное списание поставки: сколько (пусто — всё) и почему. Причина обязательна — она попадёт в журнал. */
export function WriteOffSheet({ batch, onClose }: { batch: Batch | null; onClose: () => void }) {
  const { t } = useTranslation('stock')
  return (
    <Sheet open={batch !== null} onClose={onClose} title={t('writeOff.title')} subtitle={batch?.productName}>
      {batch && <WriteOffForm key={batch.id} batch={batch} onDone={onClose} />}
    </Sheet>
  )
}

function WriteOffForm({ batch, onDone }: { batch: Batch; onDone: () => void }) {
  const { t } = useTranslation('stock')
  const { t: tf } = useTranslation('format')
  const f = useFormat()
  const errorText = useErrorText()
  const mutation = useWriteOff()
  const online = useOnline()
  const units = INPUT_UNITS[batch.unit]
  const [unit, setUnit] = useState(units[0]!.value)
  const [quantity, setQuantity] = useState('')
  const [reason, setReason] = useState<Reason>(batch.isUsable ? 'damaged' : 'spoiled')
  const [other, setOther] = useState('')
  const [error, setError] = useState<{ field?: 'quantity' | 'other'; text: string } | null>(null)
  const unitId = useId()
  const reasonName = useId()

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    const parsed = quantity.trim() === '' ? undefined : Number(quantity.replace(',', '.'))
    const factor = units.find((u) => u.value === unit)?.factor ?? 1
    if (parsed !== undefined && (!Number.isFinite(parsed) || parsed <= 0)) return setError({ field: 'quantity', text: t('writeOff.errors.quantity') })
    if (parsed !== undefined && parsed * factor > batch.remainingQty)
      return setError({ field: 'quantity', text: t('writeOff.errors.tooMuch', { left: f.qty(batch.remainingQty, batch.unit) }) })
    const reasonText = reason === 'other' ? other.trim() : t(`writeOff.reasons.${reason}`)
    if (!reasonText) return setError({ field: 'other', text: t('writeOff.errors.reason') })

    mutation.mutate(
      { batchId: batch.id, quantity: parsed, unit: parsed === undefined ? undefined : unit, reason: reasonText },
      {
        onSuccess: () => {
          const written = parsed === undefined ? batch.remainingQty : parsed * factor
          toast.show(t('writeOff.done', { product: batch.productName, qty: f.qty(written, batch.unit) }))
          onDone()
        },
        onError: (cause) => setError({ text: errorText(cause) }),
      },
    )
  }

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      <p className={styles.context}>
        {t('details.supply', { date: f.date(batch.receivedAt) })}
        {' · '}
        {t('writeOff.left', { qty: f.qty(batch.remainingQty, batch.unit) })}
      </p>

      <div className={styles.quantity}>
        <Field
          label={t('writeOff.quantity')}
          hint={t('writeOff.quantityHint')}
          inputMode="decimal"
          autoComplete="off"
          value={quantity}
          onChange={(e) => setQuantity(e.target.value)}
          error={error?.field === 'quantity' ? error.text : undefined}
        />
        {units.length > 1 && (
          <div className={styles.unit}>
            <label htmlFor={unitId} className={styles.label}>
              {t('writeOff.unit')}
            </label>
            <select id={unitId} className={styles.select} value={unit} onChange={(e) => setUnit(e.target.value)}>
              {units.map((u) => (
                <option key={u.value} value={u.value}>
                  {tf(`unit.${u.key}`)}
                </option>
              ))}
            </select>
          </div>
        )}
      </div>

      <fieldset className={styles.reasons}>
        <legend className={styles.label}>{t('writeOff.reason')}</legend>
        {REASONS.map((value) => (
          <label key={value} className={styles.radio}>
            <input type="radio" name={reasonName} value={value} checked={reason === value} onChange={() => setReason(value)} />
            <span>{t(`writeOff.reasons.${value}`)}</span>
          </label>
        ))}
      </fieldset>
      {reason === 'other' && (
        <Field
          label={t('writeOff.otherLabel')}
          value={other}
          maxLength={500}
          autoFocus
          onChange={(e) => setOther(e.target.value)}
          error={error?.field === 'other' ? error.text : undefined}
        />
      )}

      {error && !error.field && (
        <p className={styles.error} role="alert">
          {error.text}
        </p>
      )}

      <div className={styles.actions}>
        <Button variant="secondary" onClick={onDone}>
          {t('writeOff.cancel')}
        </Button>
        <Button type="submit" disabled={mutation.isPending || !online} title={online ? undefined : t('writeOff.offline')}>
          {mutation.isPending ? t('writeOff.submitting') : t('writeOff.submit')}
        </Button>
      </div>
    </form>
  )
}

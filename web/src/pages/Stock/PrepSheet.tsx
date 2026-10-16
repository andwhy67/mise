import { Leaf } from 'lucide-react'
import { useEffect, useId, useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useCookPrep, usePrepPreview, type PrepItem, type PrepPlan } from '@/api/prep'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { Sheet } from '@/components/Sheet'
import { LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import { uuid } from '@/lib/uuid'
import { useOnline } from '@/pwa/network'
import { INPUT_UNITS } from './inputUnits'
import styles from './PrepSheet.module.scss'

const PREVIEW_DELAY_MS = 300

/** Приготовить заготовку: сколько, что уйдёт со склада по FEFO, какой будет срок — и только потом списание. */
export function PrepSheet({ prep, onClose }: { prep: PrepItem | null; onClose: () => void }) {
  const { t } = useTranslation('stock')
  const f = useFormat()
  return (
    <Sheet
      open={prep !== null}
      onClose={onClose}
      title={t('prep.sheetTitle', { name: prep?.name ?? '' })}
      subtitle={prep && t('prep.recipe', { yield: f.qty(prep.yield, prep.unit), hours: prep.shelfLifeHours })}
      wide
    >
      {prep && <PrepForm key={prep.id} prep={prep} onDone={onClose} />}
    </Sheet>
  )
}

function PrepForm({ prep, onDone }: { prep: PrepItem; onDone: () => void }) {
  const { t } = useTranslation('stock')
  const { t: tf } = useTranslation('format')
  const f = useFormat()
  const errorText = useErrorText()
  const online = useOnline()
  const mutation = useCookPrep()
  const units = INPUT_UNITS[prep.unit]
  const [unit, setUnit] = useState(units[0]!.value)
  const factor = units.find((u) => u.value === unit)?.factor ?? 1
  const [quantity, setQuantity] = useState(() => String(Math.min(prep.yield, prep.maxQuantity) / units[0]!.factor))
  const [error, setError] = useState<string | null>(null)
  const unitId = useId()

  const parsed = Number(quantity.replace(',', '.'))
  const valid = quantity.trim() !== '' && Number.isFinite(parsed) && parsed > 0
  const request = useMemo(() => (valid ? { quantity: parsed, unit } : null), [valid, parsed, unit])
  const [debounced, setDebounced] = useState(request)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(request), PREVIEW_DELAY_MS)
    return () => clearTimeout(timer)
  }, [request])
  const preview = usePrepPreview(prep.id, debounced)
  // Новый ключ на каждое новое количество: повтор того же нажатия не приготовит дважды.
  const attempt = useMemo(() => ({ request, key: uuid() }), [request])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    if (!request) return setError(t('prep.errors.quantity'))
    mutation.mutate(
      { prepId: prep.id, ...request, idempotencyKey: attempt.key },
      {
        onSuccess: (result) => {
          toast.show(
            t('prep.done', {
              name: prep.name,
              qty: f.qty(result.plan.quantity, prep.unit),
              when: f.whenInline(result.plan.expiresAt),
            }),
          )
          onDone()
        },
        onError: (cause) => setError(errorText(cause)),
      },
    )
  }

  const plan = preview.data
  const canCook = !!plan && plan.canCook && !preview.isPlaceholderData && request !== null

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      <div className={styles.quantity}>
        <Field
          label={t('prep.quantity')}
          hint={t('prep.quantityHint', { max: f.qty(prep.maxQuantity, prep.unit) })}
          inputMode="decimal"
          autoComplete="off"
          value={quantity}
          onChange={(e) => setQuantity(e.target.value)}
          error={!valid && quantity.trim() !== '' ? t('prep.errors.quantity') : undefined}
        />
        {units.length > 1 && (
          <div className={styles.unit}>
            <label htmlFor={unitId} className={styles.label}>
              {t('writeOff.unit')}
            </label>
            <select
              id={unitId}
              className={styles.select}
              value={unit}
              onChange={(e) => {
                const next = units.find((u) => u.value === e.target.value)!
                if (valid) setQuantity(String((parsed * factor) / next.factor))
                setUnit(next.value)
              }}
            >
              {units.map((u) => (
                <option key={u.value} value={u.value}>
                  {tf(`unit.${u.key}`)}
                </option>
              ))}
            </select>
          </div>
        )}
      </div>

      <section className={styles.preview} aria-live="polite" aria-busy={preview.isFetching}>
        {!plan ? (
          preview.isError ? (
            <p className={styles.error}>{errorText(preview.error)}</p>
          ) : (
            <LoadingState lines={3} />
          )
        ) : (
          <PlanView plan={plan} />
        )}
      </section>

      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}

      <div className={styles.actions}>
        <Button variant="secondary" onClick={onDone}>
          {t('writeOff.cancel')}
        </Button>
        <Button type="submit" disabled={!canCook || mutation.isPending || !online} title={online ? undefined : t('prep.offline')}>
          {mutation.isPending ? t('prep.submitting') : t('prep.submit')}
        </Button>
      </div>
    </form>
  )
}

function PlanView({ plan }: { plan: PrepPlan }) {
  const { t } = useTranslation('stock')
  const f = useFormat()
  const limited = plan.limitingProductName !== null
  const when = f.when(plan.expiresAt, limited ? 'Warning' : 'Fresh')

  return (
    <>
      <div className={styles.result}>
        <span className={styles.resultLabel}>{t('prep.resultExpiry')}</span>
        <StatusMark tone={plan.canCook ? when.tone : 'muted'}>{when.text}</StatusMark>
        <p className={styles.resultNote}>
          {limited ? t('prep.limitedBy', { product: plan.limitingProductName }) : t('prep.fullShelfLife', { hours: plan.shelfLifeHours })}
        </p>
        {plan.cost !== null && <p className={styles.resultNote}>{t('prep.cost', { cost: f.money(plan.cost) })}</p>}
      </div>

      {plan.rescuedCost > 0 && (
        <p className={styles.rescued}>
          <Leaf aria-hidden="true" strokeWidth={1.8} />
          <span>{t('prep.rescued', { cost: f.money(plan.rescuedCost) })}</span>
        </p>
      )}

      <h3 className={styles.listTitle}>{t('prep.takes')}</h3>
      <ul className={styles.ingredients}>
        {plan.ingredients.map((ingredient) => (
          <li key={ingredient.productId} className={styles.ingredient}>
            <div className={styles.ingredientHead}>
              <span className={styles.ingredientName}>{ingredient.productName}</span>
              <span className={styles.ingredientQty}>{f.qty(ingredient.required, ingredient.unit)}</span>
            </div>
            {ingredient.shortage > 0 ? (
              <StatusMark tone="crit" weight="regular">
                {t('prep.short', { have: f.qty(ingredient.available, ingredient.unit), short: f.qty(ingredient.shortage, ingredient.unit) })}
              </StatusMark>
            ) : (
              <ul className={styles.batches}>
                {ingredient.batches.map((batch) => (
                  <li key={batch.batchId}>
                    <span>{t('prep.fromBatch', { qty: f.qty(batch.quantity, ingredient.unit), when: f.whenInline(batch.expiresAt) })}</span>
                    {batch.rescued && (
                      <StatusMark tone="fresh" weight="regular">
                        {t('prep.rescuedBatch')}
                      </StatusMark>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </li>
        ))}
      </ul>
    </>
  )
}

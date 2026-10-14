import { Plus, Trash2 } from 'lucide-react'
import { useMemo, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/api/errors'
import { useCreateSupply } from '@/api/mutations'
import { useProducts } from '@/api/queries'
import type { Product } from '@/api/types'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { Select, type SelectGroup } from '@/components/Select'
import { Sheet } from '@/components/Sheet'
import { ErrorState, LoadingState } from '@/components/StateBlocks'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import { uuid } from '@/lib/uuid'
import {
  defaultNumber,
  emptyRow,
  previewExpiry,
  toMoscowInput,
  toRequest,
  unitsFor,
  validateSupply,
  type SupplyErrors,
  type SupplyFormState,
  type SupplyRow,
} from './supplyForm'
import styles from './SupplyFormSheet.module.scss'

/** Строка, которую можно подставить снаружи — например, из «Что заказать». */
export interface PrefillRow {
  productId: string
  quantity: number
}

interface SupplyFormSheetProps {
  open: boolean
  onClose: () => void
  prefill?: PrefillRow[]
}

/** Приём поставки (DESIGN.md §6.3): строки продуктов, срок — «годен до» или «хранить N дней», иначе норма продукта. */
export function SupplyFormSheet({ open, onClose, prefill }: SupplyFormSheetProps) {
  const { t } = useTranslation('supplies')
  const products = useProducts()
  return (
    <Sheet open={open} onClose={onClose} title={t('form.title')} subtitle={t('form.subtitle')} wide>
      {products.isPending ? (
        <LoadingState lines={4} />
      ) : products.isError ? (
        <ErrorState error={products.error} onRetry={() => void products.refetch()} />
      ) : (
        open && <SupplyForm products={products.data} prefill={prefill} onDone={onClose} />
      )}
    </Sheet>
  )
}

let rowCounter = 0
const nextKey = () => `row-${++rowCounter}`

/** Количество из подсказки — в удобной единице: 30000 г → «30» кг. */
function prefillRow(row: PrefillRow, product: Product | undefined): SupplyRow {
  const units = unitsFor(product)
  const big = units[0] === 'Kilogram' || units[0] === 'Liter'
  return { ...emptyRow(nextKey()), productId: row.productId, unit: units[0]!, quantity: String(big ? row.quantity / 1000 : row.quantity).replace('.', ',') }
}

function SupplyForm({ products, prefill, onDone }: { products: Product[]; prefill?: PrefillRow[]; onDone: () => void }) {
  const { t } = useTranslation('supplies')
  const errorText = useErrorText()
  const mutation = useCreateSupply()
  const [form, setForm] = useState<SupplyFormState>(() => {
    const now = new Date()
    const rows = prefill?.length ? prefill.map((row) => prefillRow(row, products.find((p) => p.id === row.productId))) : [emptyRow(nextKey())]
    return { number: defaultNumber(now), supplier: '', createdAt: toMoscowInput(now), rows }
  })
  const [errors, setErrors] = useState<SupplyErrors>({})
  const [serverError, setServerError] = useState<string | null>(null)
  // Один ключ на одно содержимое формы: повтор той же отправки — тот же ключ (CLAUDE.md, правило 5).
  const idempotency = useRef<{ body: string; key: string } | null>(null)

  const groups = useMemo<SelectGroup[]>(() => {
    const byCategory = new Map<string, Product[]>()
    for (const p of products) byCategory.set(p.category ?? t('form.noCategory'), [...(byCategory.get(p.category ?? t('form.noCategory')) ?? []), p])
    return [...byCategory.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([label, items]) => ({ label, options: items.sort((a, b) => a.name.localeCompare(b.name)).map((p) => ({ value: p.id, label: p.name })) }))
  }, [products, t])

  const update = (patch: Partial<SupplyFormState>) => setForm((current) => ({ ...current, ...patch }))
  const updateRow = (key: string, patch: Partial<SupplyRow>) =>
    setForm((current) => ({ ...current, rows: current.rows.map((row) => (row.key === key ? { ...row, ...patch } : row)) }))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setServerError(null)
    const found = validateSupply(form, products)
    setErrors(found)
    if (Object.keys(found).length) return

    const body = toRequest(form)
    const serialized = JSON.stringify(body)
    if (idempotency.current?.body !== serialized) idempotency.current = { body: serialized, key: uuid() }
    mutation.mutate(
      { body, idempotencyKey: idempotency.current.key },
      {
        onSuccess: (supply) => {
          toast.show(t('form.done', { number: supply.number, count: supply.items.length }))
          onDone()
        },
        onError: (cause) => {
          if (cause instanceof ApiError && cause.code === 'supply.duplicate_number') setErrors({ number: 'duplicate' })
          else setServerError(serverMessage(cause, form, products, t) ?? errorText(cause))
        },
      },
    )
  }

  const err = (key: string) => (errors[key] ? t(`form.errors.${errors[key]}`) : undefined)

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      <div className={styles.head}>
        <Field label={t('form.number')} value={form.number} maxLength={64} onChange={(e) => update({ number: e.target.value })} error={err('number')} />
        <Field label={t('form.supplier')} value={form.supplier} maxLength={200} onChange={(e) => update({ supplier: e.target.value })} />
        <Field
          label={t('form.createdAt')}
          type="datetime-local"
          value={form.createdAt}
          onChange={(e) => update({ createdAt: e.target.value })}
          error={err('createdAt')}
          hint={t('form.createdAtHint')}
        />
      </div>

      <ol className={styles.rows} aria-label={t('form.items')}>
        {form.rows.map((row, index) => (
          <SupplyRowEditor
            key={row.key}
            index={index}
            row={row}
            product={products.find((p) => p.id === row.productId)}
            createdAt={form.createdAt}
            groups={groups}
            findProduct={(id) => products.find((p) => p.id === id)}
            error={(field) => err(`${row.key}.${field}`)}
            onChange={(patch) => updateRow(row.key, patch)}
            onRemove={form.rows.length > 1 ? () => update({ rows: form.rows.filter((r) => r.key !== row.key) }) : undefined}
          />
        ))}
      </ol>
      {errors.rows && <p className={styles.error}>{t('form.errors.empty')}</p>}

      <Button variant="text" icon={<Plus />} onClick={() => update({ rows: [...form.rows, emptyRow(nextKey())] })}>
        {t('form.addRow')}
      </Button>

      {serverError && (
        <p className={styles.error} role="alert">
          {serverError}
        </p>
      )}

      <div className={styles.actions}>
        <Button variant="secondary" onClick={onDone}>
          {t('form.cancel')}
        </Button>
        <Button type="submit" disabled={mutation.isPending}>
          {mutation.isPending ? t('form.submitting') : t('form.submit')}
        </Button>
      </div>
    </form>
  )
}

interface RowEditorProps {
  index: number
  row: SupplyRow
  product: Product | undefined
  createdAt: string
  groups: SelectGroup[]
  findProduct: (id: string) => Product | undefined
  error: (field: string) => string | undefined
  onChange: (patch: Partial<SupplyRow>) => void
  onRemove?: () => void
}

function SupplyRowEditor({ index, row, product, createdAt, groups, findProduct, error, onChange, onRemove }: RowEditorProps) {
  const { t } = useTranslation('supplies')
  const { t: tf } = useTranslation('format')
  const f = useFormat()
  const units = unitsFor(product)
  const unit = row.unit ?? units[0]!
  const preview = previewExpiry(row, product, createdAt)
  const unitKey = { Gram: 'g', Kilogram: 'kg', Milliliter: 'ml', Liter: 'l', Piece: 'pcs' } as const
  const defaultDays = product?.defaultShelfLifeHours ? f.number(product.defaultShelfLifeHours / 24) : null

  return (
    <li className={styles.row}>
      <span className={styles.rowNumber} aria-hidden="true">
        {index + 1}
      </span>
      <div className={styles.rowFields}>
        <Select
          className={styles.product}
          label={t('form.product')}
          placeholder={t('form.productPlaceholder')}
          groups={groups}
          value={row.productId}
          error={error('product')}
          // Единица сразу подставляется по продукту: в запрос уходит ровно то, что видно в списке.
          onChange={(e) => onChange({ productId: e.target.value, unit: unitsFor(findProduct(e.target.value))[0]! })}
        />
        <div className={styles.quantity}>
          <Field label={t('form.quantity')} inputMode="decimal" autoComplete="off" value={row.quantity} error={error('quantity')} onChange={(e) => onChange({ quantity: e.target.value })} />
          <Select
            className={styles.unit}
            label={t('form.unit')}
            options={units.map((u) => ({ value: u, label: tf(`unit.${unitKey[u]}`) }))}
            value={unit}
            error={error('unit')}
            onChange={(e) => onChange({ unit: e.target.value as SupplyRow['unit'] })}
          />
        </div>
        <Select
          className={styles.expiryMode}
          label={t('form.expiry')}
          options={[
            { value: 'default', label: defaultDays ? t('form.expiryDefault', { days: defaultDays }) : t('form.expiryDefaultNone') },
            { value: 'days', label: t('form.expiryDays') },
            { value: 'date', label: t('form.expiryDate') },
          ]}
          value={row.expiry}
          error={error('expiry')}
          onChange={(e) => onChange({ expiry: e.target.value as SupplyRow['expiry'] })}
        />
        {row.expiry === 'days' && (
          <Field className={styles.expiryValue} label={t('form.days')} inputMode="decimal" value={row.days} error={error('days')} onChange={(e) => onChange({ days: e.target.value })} />
        )}
        {row.expiry === 'date' && (
          <Field className={styles.expiryValue} label={t('form.date')} type="date" value={row.date} error={error('date')} onChange={(e) => onChange({ date: e.target.value })} />
        )}
      </div>
      <p className={styles.preview} aria-live="polite">
        {preview.expiresAt ? t('form.preview', { when: f.moment(preview.expiresAt), source: t(`form.source.${preview.source}`) }) : product ? t('form.previewUnknown') : ''}
      </p>
      {onRemove && (
        <button type="button" className={styles.remove} aria-label={t('form.removeRow', { index: index + 1 })} onClick={onRemove}>
          <Trash2 aria-hidden="true" strokeWidth={1.8} />
        </button>
      )}
    </li>
  )
}

/** Ошибки проверки сервера («Items[1].Quantity») — к строке по-человечески; остальное — общим текстом. */
function serverMessage(cause: unknown, form: SupplyFormState, products: Product[], t: (key: string, options?: Record<string, unknown>) => string) {
  if (!(cause instanceof ApiError)) return null
  if (cause.code?.startsWith('expiry.')) return t(`form.errors.server.${cause.code.replace('expiry.', '')}`, { defaultValue: '' }) || null
  const fields = Object.keys(cause.problem?.errors ?? {})
  const match = fields.map((key) => /^Items\[(\d+)\]/i.exec(key)).find(Boolean)
  if (match) {
    const row = form.rows[Number(match[1])]
    const name = products.find((p) => p.id === row?.productId)?.name ?? `#${Number(match[1]) + 1}`
    return t('form.errors.server.row', { product: name })
  }
  return null
}

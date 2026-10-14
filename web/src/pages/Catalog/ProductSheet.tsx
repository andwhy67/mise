import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/api/errors'
import { useDeleteProduct, useSaveProduct } from '@/api/mutations'
import { useProducts } from '@/api/queries'
import type { Product } from '@/api/types'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { Select } from '@/components/Select'
import { Sheet } from '@/components/Sheet'
import type { MeasureUnit } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { emptyProductForm, productFormToRequest, productToForm, validateProduct, type ProductForm } from './catalogForm'
import { DeleteConfirm } from './DeleteConfirm'
import styles from './CatalogForms.module.scss'

const UNITS: MeasureUnit[] = ['Gram', 'Milliliter', 'Piece']
const STORAGE = ['fridge', 'freezer', 'dry'] as const

/** Карточка продукта: единица, норма хранения, минимальный остаток (для «Что заказать»), категория. */
export function ProductSheet({ open, product, onClose }: { open: boolean; product: Product | null; onClose: () => void }) {
  const { t } = useTranslation('catalog')
  return (
    <Sheet open={open} onClose={onClose} title={product ? product.name : t('products.new')} subtitle={t('products.formHint')}>
      {open && <ProductFormBody product={product} onDone={onClose} />}
    </Sheet>
  )
}

function ProductFormBody({ product, onDone }: { product: Product | null; onDone: () => void }) {
  const { t } = useTranslation('catalog')
  const errorText = useErrorText()
  const products = useProducts().data ?? []
  const save = useSaveProduct()
  const remove = useDeleteProduct()
  const [form, setForm] = useState<ProductForm>(() => (product ? productToForm(product) : emptyProductForm()))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const categoriesId = useId()
  const storageId = useId()
  const categories = [...new Set(products.map((p) => p.category).filter(Boolean))] as string[]
  const big = form.unit !== 'Piece'
  const unitLabel = t(`units.big.${form.unit}`)
  const set = (patch: Partial<ProductForm>) => setForm((current) => ({ ...current, ...patch }))
  const err = (key: string) => (errors[key] ? t(`errors.${errors[key]}`) : undefined)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setServerError(null)
    const found = validateProduct(form)
    setErrors(found)
    if (Object.keys(found).length) return
    save.mutate(
      { id: product?.id, body: productFormToRequest(form) },
      {
        onSuccess: () => {
          toast.show(t(product ? 'saved' : 'created', { name: form.name.trim() }))
          onDone()
        },
        onError: (cause) => {
          if (cause instanceof ApiError && cause.code === 'product.duplicate_name') setErrors({ name: 'duplicateName' })
          else setServerError(errorText(cause))
        },
      },
    )
  }

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      <Field label={t('products.name')} value={form.name} maxLength={200} onChange={(e) => set({ name: e.target.value })} error={err('name')} />
      <div className={styles.pair}>
        <Select
          label={t('products.unit')}
          options={UNITS.map((u) => ({ value: u, label: t(`units.base.${u}`) }))}
          value={form.unit}
          onChange={(e) => set({ unit: e.target.value as MeasureUnit })}
          // Единицу продукта, по которому уже есть движения, сервер менять не даст — подсказываем заранее.
          disabled={product !== null}
        />
        {/* Норма короче суток (зелень, заготовки) — в часах; длиннее — в днях. */}
        <div className={styles.withUnit}>
          <Field
            label={t('products.shelfLife')}
            inputMode={form.shelfLifeUnit === 'hours' ? 'numeric' : 'decimal'}
            value={form.shelfLife}
            onChange={(e) => set({ shelfLife: e.target.value })}
            error={err('shelfLife')}
            hint={t('products.shelfLifeHint')}
          />
          <Select
            label={t('products.shelfLifeUnit')}
            hideLabel
            options={[
              { value: 'hours', label: t('products.hours') },
              { value: 'days', label: t('products.days') },
            ]}
            value={form.shelfLifeUnit}
            onChange={(e) => set({ shelfLifeUnit: e.target.value as ProductForm['shelfLifeUnit'] })}
          />
        </div>
      </div>
      <div className={styles.pair}>
        <Field
          label={t('products.minStockLabel', { unit: big ? unitLabel : t('units.big.Piece') })}
          inputMode="decimal"
          value={form.minStock}
          onChange={(e) => set({ minStock: e.target.value })}
          error={err('minStock')}
          hint={t('products.minStockHint')}
        />
        <Field
          label={t('products.packSizeLabel', { unit: big ? unitLabel : t('units.big.Piece') })}
          inputMode="decimal"
          value={form.packSize}
          onChange={(e) => set({ packSize: e.target.value })}
          error={err('packSize')}
          hint={t('products.packSizeHint')}
        />
      </div>
      <div className={styles.pair}>
        <Field label={t('products.category')} list={categoriesId} value={form.category} maxLength={100} onChange={(e) => set({ category: e.target.value })} />
        <Field label={t('products.storage')} list={storageId} value={form.storageType} maxLength={100} onChange={(e) => set({ storageType: e.target.value })} />
      </div>
      <datalist id={categoriesId}>
        {categories.map((c) => (
          <option key={c} value={c} />
        ))}
      </datalist>
      <datalist id={storageId}>
        {STORAGE.map((s) => (
          <option key={s} value={t(`storage.${s}`)} />
        ))}
      </datalist>
      {product && <p className={styles.note}>{t('products.unitLocked')}</p>}

      {serverError && (
        <p className={styles.error} role="alert">
          {serverError}
        </p>
      )}

      <div className={styles.actions}>
        {product && (
          <DeleteConfirm
            pending={remove.isPending}
            onConfirm={() =>
              remove.mutate(product.id, {
                onSuccess: () => {
                  toast.show(t('deleted', { name: product.name }), 'warn')
                  onDone()
                },
                onError: (cause) => setServerError(errorText(cause)),
              })
            }
          />
        )}
        <span className={styles.spacer} />
        <Button variant="secondary" onClick={onDone}>
          {t('cancel')}
        </Button>
        <Button type="submit" disabled={save.isPending}>
          {t('save')}
        </Button>
      </div>
    </form>
  )
}

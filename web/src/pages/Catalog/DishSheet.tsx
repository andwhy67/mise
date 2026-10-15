import { Plus, Trash2 } from 'lucide-react'
import { useId, useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/api/errors'
import { useDeleteDish, useSaveDish } from '@/api/mutations'
import { useDishes, useProducts } from '@/api/queries'
import type { Dish } from '@/api/types'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { Combobox, type ComboboxOption } from '@/components/Combobox'
import { Sheet } from '@/components/Sheet'
import { productOptions } from '@/lib/productOptions'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { dishFormToRequest, emptyDishForm, validateDish, type DishForm, type IngredientRow } from './catalogForm'
import { DeleteConfirm } from './DeleteConfirm'
import styles from './CatalogForms.module.scss'

let counter = 0
const nextKey = () => `ing-${++counter}`

/** Блюдо: цена, категория, в меню или нет, состав на одну порцию. */
export function DishSheet({ open, dish, onClose }: { open: boolean; dish: Dish | null; onClose: () => void }) {
  const { t } = useTranslation('catalog')
  return (
    <Sheet open={open} onClose={onClose} title={dish ? dish.name : t('dishes.new')} subtitle={t('dishes.formHint')} wide>
      {open && <DishFormBody dish={dish} onDone={onClose} />}
    </Sheet>
  )
}

function DishFormBody({ dish, onDone }: { dish: Dish | null; onDone: () => void }) {
  const { t } = useTranslation('catalog')
  const { t: tf } = useTranslation('format')
  const errorText = useErrorText()
  const productsData = useProducts().data
  const products = useMemo(() => productsData ?? [], [productsData])
  const dishes = useDishes().data ?? []
  const save = useSaveDish()
  const remove = useDeleteDish()
  const categoriesId = useId()
  const activeId = useId()
  const [form, setForm] = useState<DishForm>(() =>
    dish
      ? {
          name: dish.name,
          category: dish.category ?? '',
          price: String(dish.price).replace('.', ','),
          isActive: dish.isActive,
          ingredients: dish.ingredients.map((i) => ({ key: nextKey(), productId: i.productId, quantity: String(i.quantity).replace('.', ','), removable: i.removable })),
        }
      : { ...emptyDishForm(), ingredients: [{ key: nextKey(), productId: '', quantity: '' }] },
  )
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const categories = [...new Set(dishes.map((d) => d.category).filter(Boolean))] as string[]

  const groups = useMemo<ComboboxOption[]>(() => productOptions(products, t('noCategory'), (unit) => t(`units.big.${unit}`)), [products, t])

  const set = (patch: Partial<DishForm>) => setForm((current) => ({ ...current, ...patch }))
  const setRow = (key: string, patch: Partial<IngredientRow>) =>
    setForm((current) => ({ ...current, ingredients: current.ingredients.map((r) => (r.key === key ? { ...r, ...patch } : r)) }))
  const err = (key: string) => (errors[key] ? t(`errors.${errors[key]}`) : undefined)
  const unitShort = { Gram: 'g', Milliliter: 'ml', Piece: 'pcs' } as const

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setServerError(null)
    const found = validateDish(form)
    setErrors(found)
    if (Object.keys(found).length) return
    save.mutate(
      { id: dish?.id, body: dishFormToRequest(form) },
      {
        onSuccess: () => {
          toast.show(t(dish ? 'saved' : 'created', { name: form.name.trim() }))
          onDone()
        },
        onError: (cause) => {
          if (cause instanceof ApiError && cause.code === 'dish.duplicate_name') setErrors({ name: 'duplicateName' })
          else setServerError(errorText(cause))
        },
      },
    )
  }

  return (
    <form className={styles.form} onSubmit={submit} noValidate>
      <div className={styles.triple}>
        <Field label={t('dishes.name')} value={form.name} maxLength={200} onChange={(e) => set({ name: e.target.value })} error={err('name')} />
        <Field label={t('dishes.category')} list={categoriesId} value={form.category} maxLength={100} onChange={(e) => set({ category: e.target.value })} />
        <Field label={t('dishes.price')} inputMode="decimal" value={form.price} onChange={(e) => set({ price: e.target.value })} error={err('price')} />
      </div>
      <datalist id={categoriesId}>
        {categories.map((c) => (
          <option key={c} value={c} />
        ))}
      </datalist>

      <label className={styles.checkbox} htmlFor={activeId}>
        <input id={activeId} type="checkbox" checked={form.isActive} onChange={(e) => set({ isActive: e.target.checked })} />
        <span>
          {t('dishes.active')}
          <span className={styles.checkboxHint}>{t('dishes.activeHint')}</span>
        </span>
      </label>

      <fieldset className={styles.ingredients}>
        <legend className={styles.legend}>{t('dishes.composition')}</legend>
        <p className={styles.note}>{t('dishes.compositionHint')}</p>
        <ol className={styles.ingredientList}>
          {form.ingredients.map((row, index) => {
            const product = products.find((p) => p.id === row.productId)
            return (
              <li key={row.key} className={styles.ingredient}>
                <Combobox
                  label={t('dishes.product')}
                  hideLabel={index > 0}
                  placeholder={t('dishes.productPlaceholder')}
                  options={groups}
                  value={row.productId}
                  error={err(`${row.key}.product`)}
                  onChange={(productId) => setRow(row.key, { productId })}
                />
                <Field
                  label={t('dishes.perPortion')}
                  hideLabel={index > 0}
                  inputMode="decimal"
                  value={row.quantity}
                  error={err(`${row.key}.quantity`)}
                  onChange={(e) => setRow(row.key, { quantity: e.target.value })}
                />
                {/* Единица — у каждой строки своя: молоко в мл, яйца в штуках. */}
                <span className={`${styles.unit} ${index === 0 ? styles.offsetFirst : ''}`}>{product ? tf(`unit.${unitShort[product.unit]}`) : ''}</span>
                {/* «Можно убрать»: гость и повар смогут заказать «без …», а когда продукт кончился — блюдо подадут без него. */}
                <label className={`${styles.removable} ${index === 0 ? styles.offsetFirst : ''}`} title={t('dishes.removableHint')} aria-label={t('dishes.removable')}>
                  <input type="checkbox" checked={row.removable ?? false} onChange={(e) => setRow(row.key, { removable: e.target.checked })} />
                  <span>{t('dishes.removable')}</span>
                </label>
                <button
                  type="button"
                  className={`${styles.remove} ${index === 0 ? styles.offsetFirst : ''}`}
                  aria-label={t('dishes.removeIngredient', { name: product?.name ?? index + 1 })}
                  onClick={() => set({ ingredients: form.ingredients.filter((r) => r.key !== row.key) })}
                >
                  <Trash2 aria-hidden="true" strokeWidth={1.8} />
                </button>
              </li>
            )
          })}
        </ol>
        {errors.ingredients && <p className={styles.error}>{t('errors.emptyComposition')}</p>}
        <Button variant="text" icon={<Plus />} onClick={() => set({ ingredients: [...form.ingredients, { key: nextKey(), productId: '', quantity: '' }] })}>
          {t('dishes.addIngredient')}
        </Button>
      </fieldset>

      {serverError && (
        <p className={styles.error} role="alert">
          {serverError}
        </p>
      )}

      <div className={styles.actions}>
        {dish && (
          <DeleteConfirm
            pending={remove.isPending}
            onConfirm={() =>
              remove.mutate(dish.id, {
                onSuccess: () => {
                  toast.show(t('deleted', { name: dish.name }), 'warn')
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

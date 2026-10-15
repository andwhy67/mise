import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { DishAvailability } from '@/api/types'
import { Button } from '@/components/Button'
import { Stepper } from '@/components/Stepper'
import { useFormat } from '@/lib/useFormat'
import { fittingSubstitutes, lineKey, missingWithout, type LineChange } from './cart'
import styles from './DishCustomizer.module.scss'

/** Порций за раз в одной строке — как на сервере (MaxPortionsPerLine). */
const MAX_PER_LINE = 100

/**
 * «Изменить блюдо» — как на киоске: каждый ингредиент можно убрать (если он «можно убрать») или заменить
 * (правила справочника, если заменителя хватает). Закончившееся отмечено: без его замены или удаления блюдо не добавить.
 * Общий для повара и гостевого меню; цена от изменений не меняется.
 */
export function DishCustomizer({
  dish,
  initial = [],
  onAdd,
}: {
  dish: DishAvailability
  initial?: LineChange[]
  onAdd: (key: string, quantity: number) => void
}) {
  const { t } = useTranslation('orders')
  const f = useFormat()
  const [changes, setChanges] = useState<LineChange[]>(initial)
  const [quantity, setQuantity] = useState(1)
  const groupId = useId()
  const missing = missingWithout(dish, changes)

  const set = (productId: string, change: LineChange | null) =>
    setChanges((current) => [...current.filter((c) => c.productId !== productId), ...(change ? [change] : [])])

  return (
    <div className={styles.customizer}>
      <ul className={styles.ingredients} aria-label={t('customize.composition')}>
        {dish.ingredients.map((ingredient) => {
          const change = changes.find((c) => c.productId === ingredient.productId)
          const out = ingredient.usable < ingredient.qtyPerPortion
          const options = fittingSubstitutes(ingredient)
          const name = `${groupId}-${ingredient.productId}`
          return (
            <li key={ingredient.productId} className={`${styles.ingredient} ${change?.kind === 'Remove' ? styles.removed : ''}`}>
              <span className={styles.name}>
                {ingredient.productName}
                {out && <span className={styles.out}>{t('customize.out')}</span>}
              </span>
              <span className={styles.choices} role="radiogroup" aria-label={ingredient.productName}>
                {!out && (
                  <label className={styles.choice}>
                    <input type="radio" name={name} checked={!change} onChange={() => set(ingredient.productId, null)} />
                    {t('customize.keep')}
                  </label>
                )}
                {ingredient.removable && (
                  <label className={styles.choice}>
                    <input
                      type="radio"
                      name={name}
                      checked={change?.kind === 'Remove'}
                      onChange={() => set(ingredient.productId, { productId: ingredient.productId, kind: 'Remove' })}
                    />
                    {t('customize.remove')}
                  </label>
                )}
                {options.map((option) => (
                  <label key={option.productId} className={styles.choice}>
                    <input
                      type="radio"
                      name={name}
                      checked={change?.kind === 'Replace' && change.substituteId === option.productId}
                      onChange={() => set(ingredient.productId, { productId: ingredient.productId, kind: 'Replace', substituteId: option.productId })}
                    />
                    {t('customize.replace', { substitute: option.productName })}
                  </label>
                ))}
                {out && !ingredient.removable && options.length === 0 && <span className={styles.noOption}>{t('customize.noOption')}</span>}
              </span>
            </li>
          )
        })}
      </ul>

      {missing.length > 0 && (
        <p className={styles.hint} role="status">
          {t('customize.needChange', { products: missing.join(', ') })}
        </p>
      )}

      <div className={styles.footer}>
        <Stepper value={quantity} min={1} max={MAX_PER_LINE} label={dish.dishName} onChange={setQuantity} />
        <Button disabled={missing.length > 0} onClick={() => onAdd(lineKey(dish.dishId, changes), quantity)}>
          {t('customize.add', { price: f.money(dish.price * quantity) })}
        </Button>
      </div>
    </div>
  )
}

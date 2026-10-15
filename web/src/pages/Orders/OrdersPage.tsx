import { ChevronDown, Search, ShoppingBag } from 'lucide-react'
import { useId, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/api/errors'
import { useCreateOrder } from '@/api/mutations'
import { useMenu, useProducts } from '@/api/queries'
import type { DishAvailability, MissingIngredient, Product } from '@/api/types'
import { Button } from '@/components/Button'
import { Meter } from '@/components/Meter'
import { Page } from '@/components/Page'
import { ScrollArea } from '@/components/ScrollArea'
import { Panel } from '@/components/Panel'
import { Sheet } from '@/components/Sheet'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { Stepper } from '@/components/Stepper'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import { uuid } from '@/lib/uuid'
import { vibrate } from '@/pwa/haptics'
import { useOnline } from '@/pwa/network'
import { activeSubstitutions, cartShortages, cartTotals, cartUsage, type Cart, type CartSubstitution } from './cart'
import { RecentOrders, type PlacedOrder } from './RecentOrders'
import styles from './OrdersPage.module.scss'

/** Порций в одной строке заказа — как на сервере (CreateOrderRequestValidator.MaxPortionsPerLine). */
const MAX_PER_LINE = 100
/** «Осталось мало» — подсвечиваем янтарным, когда порций не больше этого. */
const FEW_PORTIONS = 5

/**
 * Заказы (DESIGN.md §6.3): слева меню (сколько можно приготовить и из чего), справа корзина с проверкой
 * «на всё вместе» и заказы в работе — от «Принят» до «Выдан».
 */
export function OrdersPage() {
  const { t } = useTranslation('orders')
  const menu = useMenu()
  const [cart, setCart] = useState<Cart>(new Map())
  // Замены, на которые согласился повар: живут вместе с корзиной и уходят с ней в заказ.
  const [substitutions, setSubstitutions] = useState<CartSubstitution[]>([])
  const [cartOpen, setCartOpen] = useState(false)
  // Только что принятый заказ: подсвечивается в «Заказах в работе» вместо тоста — повар и так знает, что нажал.
  const [placed, setPlaced] = useState<PlacedOrder | null>(null)
  const products = useProducts().data ?? []
  const substitution = {
    list: substitutions,
    add: (s: CartSubstitution) => setSubstitutions((current) => [...current.filter((c) => c.productId !== s.productId), s]),
    remove: (productId: string) => setSubstitutions((current) => current.filter((c) => c.productId !== productId)),
  }
  const clear = () => {
    setCart(new Map())
    setSubstitutions([])
  }

  const setQuantity = (dishId: string, quantity: number) =>
    setCart((current) => {
      const next = new Map(current)
      if (quantity > 0) next.set(dishId, quantity)
      else next.delete(dishId)
      return next
    })

  const dishes = menu.data ?? []
  const totals = cartTotals(cart, dishes)

  return (
    <Page title={t('title')} subtitle={t('subtitle')}>
      <div className={styles.columns}>
        <Panel title={t('menu.title')} subtitle={t('menu.subtitle')} titleId="orders-menu">
          {menu.isPending ? (
            <LoadingState lines={6} />
          ) : menu.isError ? (
            <ErrorState error={menu.error} onRetry={() => void menu.refetch()} />
          ) : dishes.length === 0 ? (
            <EmptyState title={t('menu.empty')}>{t('menu.emptyHint')}</EmptyState>
          ) : (
            <MenuList dishes={dishes} cart={cart} products={products} substitution={substitution} onChange={setQuantity} />
          )}
        </Panel>

        {/* Справа — всё про заказы: собрать новый и вести принятые до выдачи. */}
        <div className={styles.side}>
          <Panel title={t('cart.title')} titleId="orders-cart" className={styles.cartPanel}>
            <CartContent cart={cart} dishes={dishes} substitution={substitution} onChange={setQuantity} onPlaced={setPlaced} onDone={clear} />
          </Panel>
          <RecentOrders placed={placed} />
        </div>
      </div>

      {/* Телефон: корзина — полоса над нижней навигацией, открывает лист (DESIGN.md §3). */}
      {totals.portions > 0 && <div className={styles.barSpacer} aria-hidden="true" />}
      {totals.portions > 0 && (
        <div className={styles.mobileBar}>
          <span className={styles.mobileSummary}>
            <ShoppingBag aria-hidden="true" strokeWidth={1.8} />
            {t('cart.portions', { count: totals.portions })} · <MoneyText value={totals.total} />
          </span>
          <Button onClick={() => setCartOpen(true)}>{t('cart.open')}</Button>
        </div>
      )}
      <Sheet open={cartOpen} onClose={() => setCartOpen(false)} title={t('cart.title')}>
        <CartContent
          cart={cart}
          dishes={dishes}
          substitution={substitution}
          onChange={setQuantity}
          onPlaced={setPlaced}
          onDone={() => {
            clear()
            setCartOpen(false)
          }}
        />
      </Sheet>
    </Page>
  )
}

function MoneyText({ value }: { value: number }) {
  const f = useFormat()
  return <>{f.money(value)}</>
}

interface SubstitutionState {
  list: CartSubstitution[]
  add: (s: CartSubstitution) => void
  remove: (productId: string) => void
}

/**
 * Чем заменить продукт, на котором блюдо в стопе: первое правило справочника, заменителя которого хватает
 * хотя бы на порцию (по остаткам меню); если остаток заменителя неизвестен — всё равно предлагаем, решит сервер.
 */
function substituteFor(dish: DishAvailability, products: Product[], menu: DishAvailability[]): CartSubstitution | null {
  const limiting = dish.limitingProduct
  if (dish.status !== 'StopShortage' || !limiting) return null
  const rules = products.find((p) => p.id === limiting.productId)?.substitutes ?? []
  const usable = new Map(menu.flatMap((d) => d.ingredients.map((i) => [i.productId, i.usable] as const)))
  const fits = rules.filter((r) => (usable.get(r.productId) ?? Infinity) >= limiting.qtyPerPortion * r.ratio)
  const rule = fits[0]
  return rule
    ? { productId: limiting.productId, productName: limiting.productName, substituteId: rule.productId, substituteName: rule.productName, unit: rule.unit, ratio: rule.ratio }
    : null
}

function MenuList({
  dishes,
  cart,
  products,
  substitution,
  onChange,
}: {
  dishes: DishAvailability[]
  cart: Cart
  products: Product[]
  substitution: SubstitutionState
  onChange: (dishId: string, quantity: number) => void
}) {
  const { t } = useTranslation('orders')
  const [search, setSearch] = useState('')
  const searchId = useId()
  const needle = search.trim().toLocaleLowerCase()

  // По категориям; внутри — сначала то, что можно заказать.
  const groups = useMemo(() => {
    const visible = dishes.filter((d) => d.isActive || d.inStopList).filter((d) => !needle || d.dishName.toLocaleLowerCase().includes(needle))
    const byCategory = new Map<string, DishAvailability[]>()
    for (const dish of visible) byCategory.set(dish.category ?? '', [...(byCategory.get(dish.category ?? '') ?? []), dish])
    return [...byCategory.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([category, items]) => ({
        category,
        items: items.sort((a, b) => Number(a.inStopList) - Number(b.inStopList) || a.dishName.localeCompare(b.dishName)),
      }))
  }, [dishes, needle])

  return (
    <>
      <div className={styles.search}>
        <label htmlFor={searchId} className="visually-hidden">
          {t('menu.search')}
        </label>
        <Search aria-hidden="true" strokeWidth={1.8} />
        <input id={searchId} type="search" placeholder={t('menu.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
      </div>
      {groups.length === 0 ? (
        <EmptyState title={t('menu.notFound')} />
      ) : (
        <ScrollArea label={t('menu.title')}>
          {groups.map((group) => (
            <section key={group.category} className={styles.category} aria-label={group.category || t('menu.other')}>
              <h3 className={styles.categoryTitle}>{group.category || t('menu.other')}</h3>
              <ul className={styles.dishes}>
                {group.items.map((dish) => (
                  <DishRow
                    key={dish.dishId}
                    dish={dish}
                    quantity={cart.get(dish.dishId) ?? 0}
                    offer={substituteFor(dish, products, dishes)}
                    substitution={substitution}
                    onChange={(q) => onChange(dish.dishId, q)}
                  />
                ))}
              </ul>
            </section>
          ))}
        </ScrollArea>
      )}
    </>
  )
}

function DishRow({
  dish,
  quantity,
  offer,
  substitution,
  onChange,
}: {
  dish: DishAvailability
  quantity: number
  /** Блюдо в стопе, но ограничивающий продукт можно заменить. */
  offer: CartSubstitution | null
  substitution: SubstitutionState
  onChange: (quantity: number) => void
}) {
  const { t } = useTranslation('orders')
  const f = useFormat()
  // Повар уже согласился на замену продукта, из-за которого блюдо в стопе, — блюдо можно добавлять (сколько — решит сервер).
  const substituted = offer !== null && substitution.list.some((s) => s.productId === offer.productId)
  const stopped = (dish.inStopList || dish.maxPortions <= 0) && !substituted
  const [open, setOpen] = useState(false)
  const compositionId = useId()

  let status
  if (dish.status === 'StopManual' || !dish.isActive) status = <StatusMark tone="crit" weight="regular">{t('menu.manual')}</StatusMark>
  else if (stopped)
    status = (
      <StatusMark tone="crit" weight="regular">
        {dish.limitingProduct ? t('menu.outOf', { product: dish.limitingProduct.productName }) : t('menu.unavailable')}
      </StatusMark>
    )
  else
    status = (
      <StatusMark tone={dish.maxPortions <= FEW_PORTIONS ? 'warn' : 'fresh'} weight="regular">
        {t('menu.canCook', { portions: f.portionsEnoughFor(dish.maxPortions) })}
      </StatusMark>
    )

  return (
    <li className={`${styles.dish} ${stopped ? styles.stopped : ''} ${quantity > 0 ? styles.inCart : ''}`}>
      <div className={styles.dishInfo}>
        <span className={styles.dishName}>{dish.dishName}</span>
        <span className={styles.dishMeta}>
          <span className={styles.price}>{f.money(dish.price)}</span>
          {status}
        </span>
        <button type="button" className={styles.compositionToggle} aria-expanded={open} aria-controls={compositionId} onClick={() => setOpen(!open)}>
          {open ? t('menu.hideComposition') : t('menu.composition')}
          <ChevronDown aria-hidden="true" strokeWidth={1.8} />
        </button>
      </div>
      {stopped && offer ? (
        <Button
          variant="secondary"
          className={styles.add}
          onClick={() => {
            substitution.add(offer)
            onChange(1)
          }}
          aria-label={t('menu.addWithSubstituteLabel', { dish: dish.dishName, product: offer.productName, substitute: offer.substituteName })}
        >
          {t('menu.addWithSubstitute', { substitute: offer.substituteName })}
        </Button>
      ) : stopped ? (
        <span className={styles.stopNote}>{t('menu.stop')}</span>
      ) : quantity === 0 ? (
        <Button variant="secondary" className={styles.add} onClick={() => onChange(1)} aria-label={t('menu.addLabel', { dish: dish.dishName })}>
          {t('menu.add')}
        </Button>
      ) : (
        <Stepper value={quantity} label={dish.dishName} max={substituted ? MAX_PER_LINE : Math.min(MAX_PER_LINE, dish.maxPortions)} onChange={onChange} />
      )}
      {open && (
        <div id={compositionId} className={styles.composition}>
          <Composition dish={dish} />
        </div>
      )}
    </li>
  )
}

/**
 * «Из чего»: каждый продукт блюда — сколько нужно на порцию, сколько есть и на сколько порций хватит.
 * Продукт, на котором блюдо упирается, выделен: видно, чего докупить, без перехода на склад.
 */
function Composition({ dish }: { dish: DishAvailability }) {
  const { t } = useTranslation('orders')
  const f = useFormat()
  const limiting = dish.limitingProduct?.productId
  return (
    <table className={styles.compositionTable}>
      <thead>
        <tr>
          <th scope="col">{t('composition.product')}</th>
          <th scope="col">{t('composition.perPortion')}</th>
          <th scope="col">{t('composition.inStock')}</th>
          <th scope="col">{t('composition.enough')}</th>
        </tr>
      </thead>
      <tbody>
        {[...dish.ingredients]
          .sort((a, b) => a.portions - b.portions)
          .map((ingredient) => {
            const tone = ingredient.portions <= 0 ? 'crit' : ingredient.portions <= FEW_PORTIONS ? 'warn' : 'fresh'
            return (
              <tr key={ingredient.productId} className={ingredient.productId === limiting ? styles.limiting : undefined}>
                <th scope="row">{ingredient.productName}</th>
                <td>{f.qty(ingredient.qtyPerPortion, ingredient.unit)}</td>
                <td>{f.qty(ingredient.usable, ingredient.unit)}</td>
                <td>
                  <StatusMark tone={tone} weight={ingredient.productId === limiting ? 'strong' : 'regular'}>
                    {t('composition.portions', { count: ingredient.portions })}
                  </StatusMark>
                </td>
              </tr>
            )
          })}
      </tbody>
    </table>
  )
}

function CartContent({
  cart,
  dishes,
  substitution,
  onChange,
  onPlaced,
  onDone,
}: {
  cart: Cart
  dishes: DishAvailability[]
  substitution: SubstitutionState
  onChange: (dishId: string, quantity: number) => void
  onPlaced: (order: PlacedOrder) => void
  onDone: () => void
}) {
  const { t } = useTranslation('orders')
  const f = useFormat()
  const errorText = useErrorText()
  const mutation = useCreateOrder()
  const online = useOnline()
  // Отказ сервера относится к конкретному составу корзины: поменяли состав — подсказка уходит.
  const active = activeSubstitutions(cart, dishes, substitution.list)
  // Состав заказа = блюда + замены: поменяли что-то из этого — прежний отказ и прежний ключ не подходят.
  const basis = JSON.stringify([[...cart.entries()], active.map((s) => [s.productId, s.substituteId])])
  const [refusal, setRefusal] = useState<{ basis: string; missing: MissingIngredient[] } | null>(null)
  const refused = refusal?.basis === basis ? refusal.missing : null
  // Один ключ на один состав: повтор той же отправки (обрыв связи, двойной клик) не создаст второй заказ.
  const idempotency = useRef<{ basis: string; key: string } | null>(null)

  const lines = [...cart.entries()].map(([dishId, quantity]) => ({ dish: dishes.find((d) => d.dishId === dishId), quantity })).filter((l) => l.dish)
  const totals = cartTotals(cart, dishes)
  const shortages = cartShortages(cart, dishes, active)
  const usage = cartUsage(cart, dishes, active)
  const missing = refused ?? (shortages.length ? shortages : null)

  if (lines.length === 0) return <EmptyState title={t('cart.empty')}>{t('cart.emptyHint')}</EmptyState>

  const submit = () => {
    if (idempotency.current?.basis !== basis) idempotency.current = { basis, key: uuid() }
    mutation.mutate(
      {
        items: lines.map((l) => ({ dishId: l.dish!.dishId, quantity: l.quantity })),
        substitutions: active.length ? active.map((s) => ({ productId: s.productId, substituteId: s.substituteId })) : undefined,
        idempotencyKey: idempotency.current.key,
      },
      {
        onSuccess: (order) => {
          vibrate('success')
          onPlaced({ id: order.id, number: order.number, total: order.total })
          onDone()
        },
        onError: (cause) => {
          vibrate('error')
          const fromServer = cause instanceof ApiError ? (cause.problem?.missing as MissingIngredient[] | undefined) : undefined
          if (fromServer?.length) setRefusal({ basis, missing: fromServer })
          else toast.show(errorText(cause), 'crit')
        },
      },
    )
  }

  return (
    <div className={styles.cart}>
      <ul className={styles.lines}>
        {lines.map(({ dish, quantity }) => (
          <li key={dish!.dishId} className={styles.line}>
            <span className={styles.lineName}>{dish!.dishName}</span>
            <Stepper value={quantity} label={dish!.dishName} max={Math.min(MAX_PER_LINE, Math.max(quantity, dish!.maxPortions))} onChange={(q) => onChange(dish!.dishId, q)} />
            <span className={styles.lineAmount}>{f.money(dish!.price * quantity)}</span>
          </li>
        ))}
      </ul>

      {/* Хватит ли продуктов: какую часть остатка заберёт заказ — запас виден до отправки, а не только «хватит/нет». */}
      <section className={styles.usage} aria-labelledby="cart-usage">
        <h3 id="cart-usage" className={styles.usageTitle}>
          {t('usage.title')}
        </h3>
        {usage.map((u) => (
          <Meter
            key={u.productId}
            label={u.productName}
            value={t('usage.value', { need: f.qty(u.required, u.unit), have: f.qty(Math.max(0, u.available), u.unit) })}
            fraction={u.share}
            tone={u.share > 1 ? 'crit' : u.share >= 0.7 ? 'warn' : 'fresh'}
          />
        ))}
      </section>

      <p className={styles.total}>
        <span>{t('cart.total', { count: totals.portions })}</span>
        <strong>{f.money(totals.total)}</strong>
      </p>

      {active.length > 0 && (
        <ul className={styles.substitutions} aria-label={t('substitute.applied')}>
          {active.map((s) => (
            <li key={s.productId}>
              <span>{t('substitute.line', { product: s.productName, substitute: s.substituteName, ratio: f.number(s.ratio) })}</span>
              <Button variant="text" onClick={() => substitution.remove(s.productId)}>
                {t('substitute.undo')}
              </Button>
            </li>
          ))}
        </ul>
      )}

      {missing && (
        <div className={styles.missing} role="alert">
          <StatusMark tone="crit">{refused ? t('cart.refused') : t('cart.notEnough')}</StatusMark>
          <ul>
            {missing.map((m) => (
              <li key={m.productId}>
                {t('cart.missingLine', {
                  product: m.productName,
                  need: f.qty(m.required, m.unit),
                  have: f.qty(Math.max(0, m.available), m.unit),
                  dishes: m.dishes.join(', '),
                })}
                {/* Замена — только по явному согласию повара: кнопка, а не автоматическая подмена. */}
                {m.substitutes?.map((offer) =>
                  offer.enough ? (
                    <Button
                      key={offer.productId}
                      variant="text"
                      className={styles.substituteOffer}
                      onClick={() =>
                        substitution.add({
                          productId: m.productId,
                          productName: m.productName,
                          substituteId: offer.productId,
                          substituteName: offer.productName,
                          unit: offer.unit,
                          ratio: offer.ratio,
                        })
                      }
                    >
                      {t('substitute.offer', { substitute: offer.productName, need: f.qty(offer.required, offer.unit), have: f.qty(offer.available, offer.unit) })}
                    </Button>
                  ) : (
                    <span key={offer.productId} className={styles.substituteShort}>
                      {t('substitute.short', { substitute: offer.productName, have: f.qty(offer.available, offer.unit) })}
                    </span>
                  ),
                )}
              </li>
            ))}
          </ul>
          <p className={styles.missingHint}>{t('cart.missingHint')}</p>
        </div>
      )}

      {!online && <p className={styles.offlineNote}>{t('cart.offline')}</p>}
      <Button size="lg" block disabled={mutation.isPending || !online} onClick={submit}>
        {mutation.isPending ? t('cart.submitting') : t('cart.submit')}
      </Button>
    </div>
  )
}

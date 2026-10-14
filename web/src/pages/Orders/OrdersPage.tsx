import { Search, ShoppingBag } from 'lucide-react'
import { useId, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/api/errors'
import { useCreateOrder } from '@/api/mutations'
import { useMenu } from '@/api/queries'
import type { DishAvailability, MissingIngredient } from '@/api/types'
import { Button } from '@/components/Button'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { Sheet } from '@/components/Sheet'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { Stepper } from '@/components/Stepper'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import { uuid } from '@/lib/uuid'
import { cartShortages, cartTotals, type Cart } from './cart'
import { RecentOrders } from './RecentOrders'
import styles from './OrdersPage.module.scss'

/** Порций в одной строке заказа — как на сервере (CreateOrderRequestValidator.MaxPortionsPerLine). */
const MAX_PER_LINE = 100
/** «Осталось мало» — подсвечиваем янтарным, когда порций не больше этого. */
const FEW_PORTIONS = 5

/** Заказы (DESIGN.md §6.3): меню с тем, сколько можно приготовить, корзина с проверкой «на всё вместе», последние заказы. */
export function OrdersPage() {
  const { t } = useTranslation('orders')
  const menu = useMenu()
  const [cart, setCart] = useState<Cart>(new Map())
  const [cartOpen, setCartOpen] = useState(false)

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
            <MenuList dishes={dishes} cart={cart} onChange={setQuantity} />
          )}
        </Panel>

        <div className={styles.cartColumn}>
          <Panel title={t('cart.title')} titleId="orders-cart">
            <CartContent cart={cart} dishes={dishes} onChange={setQuantity} onDone={() => setCart(new Map())} />
          </Panel>
        </div>
      </div>

      <RecentOrders />

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
          onChange={setQuantity}
          onDone={() => {
            setCart(new Map())
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

function MenuList({ dishes, cart, onChange }: { dishes: DishAvailability[]; cart: Cart; onChange: (dishId: string, quantity: number) => void }) {
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
        groups.map((group) => (
          <section key={group.category} className={styles.category} aria-label={group.category || t('menu.other')}>
            <h3 className={styles.categoryTitle}>{group.category || t('menu.other')}</h3>
            <ul className={styles.dishes}>
              {group.items.map((dish) => (
                <DishRow key={dish.dishId} dish={dish} quantity={cart.get(dish.dishId) ?? 0} onChange={(q) => onChange(dish.dishId, q)} />
              ))}
            </ul>
          </section>
        ))
      )}
    </>
  )
}

function DishRow({ dish, quantity, onChange }: { dish: DishAvailability; quantity: number; onChange: (quantity: number) => void }) {
  const { t } = useTranslation('orders')
  const f = useFormat()
  const stopped = dish.inStopList || dish.maxPortions <= 0

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
      </div>
      {stopped ? (
        <span className={styles.stopNote}>{t('menu.stop')}</span>
      ) : quantity === 0 ? (
        <Button variant="secondary" className={styles.add} onClick={() => onChange(1)} aria-label={t('menu.addLabel', { dish: dish.dishName })}>
          {t('menu.add')}
        </Button>
      ) : (
        <Stepper value={quantity} label={dish.dishName} max={Math.min(MAX_PER_LINE, dish.maxPortions)} onChange={onChange} />
      )}
    </li>
  )
}

function CartContent({
  cart,
  dishes,
  onChange,
  onDone,
}: {
  cart: Cart
  dishes: DishAvailability[]
  onChange: (dishId: string, quantity: number) => void
  onDone: () => void
}) {
  const { t } = useTranslation('orders')
  const f = useFormat()
  const errorText = useErrorText()
  const mutation = useCreateOrder()
  // Отказ сервера относится к конкретному составу корзины: поменяли состав — подсказка уходит.
  const [refusal, setRefusal] = useState<{ cart: Cart; missing: MissingIngredient[] } | null>(null)
  const refused = refusal?.cart === cart ? refusal.missing : null
  // Один ключ на один состав корзины: повтор той же отправки (обрыв связи, двойной клик) не создаст второй заказ.
  const idempotency = useRef<{ cart: Cart; key: string } | null>(null)

  const lines = [...cart.entries()].map(([dishId, quantity]) => ({ dish: dishes.find((d) => d.dishId === dishId), quantity })).filter((l) => l.dish)
  const totals = cartTotals(cart, dishes)
  const shortages = cartShortages(cart, dishes)
  const missing = refused ?? (shortages.length ? shortages : null)

  if (lines.length === 0) return <EmptyState title={t('cart.empty')}>{t('cart.emptyHint')}</EmptyState>

  const submit = () => {
    if (idempotency.current?.cart !== cart) idempotency.current = { cart, key: uuid() }
    mutation.mutate(
      { items: lines.map((l) => ({ dishId: l.dish!.dishId, quantity: l.quantity })), idempotencyKey: idempotency.current.key },
      {
        onSuccess: (order) => {
          toast.show(t('cart.done', { number: order.number, total: f.money(order.total) }))
          onDone()
        },
        onError: (cause) => {
          const fromServer = cause instanceof ApiError ? (cause.problem?.missing as MissingIngredient[] | undefined) : undefined
          if (fromServer?.length) setRefusal({ cart, missing: fromServer })
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

      <p className={styles.total}>
        <span>{t('cart.total', { count: totals.portions })}</span>
        <strong>{f.money(totals.total)}</strong>
      </p>

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
              </li>
            ))}
          </ul>
          <p className={styles.missingHint}>{t('cart.missingHint')}</p>
        </div>
      )}

      <Button size="lg" block disabled={mutation.isPending} onClick={submit}>
        {mutation.isPending ? t('cart.submitting') : t('cart.submit')}
      </Button>
    </div>
  )
}

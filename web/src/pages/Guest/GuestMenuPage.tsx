import { ShoppingBag } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { ApiError } from '@/api/errors'
import { useGuestMenu, usePlaceGuestOrder, type GuestDish, type GuestMenu, type PaymentMethod } from '@/api/guest'
import { Button } from '@/components/Button'
import { QrCode } from '@/components/QrCode'
import { Select } from '@/components/Select'
import { Sheet } from '@/components/Sheet'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { Stepper } from '@/components/Stepper'
import { useFormat } from '@/lib/useFormat'
import { uuid } from '@/lib/uuid'
import { lineKey, orderItems, type Cart, type LineChange } from '@/pages/Orders/cart'
import { DishCustomizer } from '@/pages/Orders/DishCustomizer'
import { GuestShell } from './GuestShell'
import {
  byCategory,
  cartCount,
  cartLines,
  cartTotal,
  changeText,
  isGuestCustomizable,
  lastOrder,
  lineBlocked,
  loadCart,
  rememberOrder,
  saveCart,
  suggestedChanges,
  tableFromQuery,
  toAvailability,
} from './guestMenu'
import styles from './GuestMenuPage.module.scss'

type Translate = (key: string, options?: Record<string, unknown>) => string

/** QR-меню гостя: актуальное меню без входа, корзина, изменения блюда, оплата (имитация) и заказ на кухню. */
export function GuestMenuPage() {
  const { key = '' } = useParams()
  const [params] = useSearchParams()
  const { t } = useTranslation(['guest', 'orders'])
  const menu = useGuestMenu(key)
  const [cart, setCart] = useState<Cart>(() => loadCart(key))
  const [customizing, setCustomizing] = useState<{ dish: GuestDish; initial: LineChange[] } | null>(null)
  const [cartOpen, setCartOpen] = useState(false)
  const f = useFormat()

  useEffect(() => saveCart(key, cart), [key, cart])

  if (menu.isPending)
    return (
      <GuestShell>
        <LoadingState lines={8} />
      </GuestShell>
    )
  if (menu.error)
    return (
      <GuestShell>
        {menu.error instanceof ApiError && menu.error.status === 404 ? (
          <EmptyState title={t('notFound.title')}>{t('notFound.text')}</EmptyState>
        ) : (
          <ErrorState error={menu.error} onRetry={() => void menu.refetch()} />
        )}
      </GuestShell>
    )

  const data = menu.data
  const table = tableFromQuery(params.get('t'), data.tables)
  const lines = cartLines(cart, data)
  const setQuantity = (lineId: string, quantity: number) =>
    setCart((current) => {
      const next = new Map(current)
      if (quantity > 0) next.set(lineId, quantity)
      else next.delete(lineId)
      return next
    })
  const addLine = (lineId: string, quantity = 1) => setQuantity(lineId, (cart.get(lineId) ?? 0) + quantity)
  const recommended = data.dishes.filter((d) => d.recommended)
  const sections = byCategory(data.dishes)
  const order = lastOrder(key)

  return (
    <GuestShell
      restaurant={data.restaurant}
      table={table}
      action={
        order && (
          <Link className={styles.myOrder} to={`/m/${key}/order/${order}`}>
            {t('myOrder')}
          </Link>
        )
      }
    >
      {!data.ordering && (
        <p className={styles.viewOnly} role="status">
          <strong>{t('viewOnly.title')}</strong> {t('viewOnly.text')}
        </p>
      )}

      {sections.length > 1 && (
        <nav className={styles.categories} aria-label={t('categories')}>
          {sections.map((section, index) => (
            <a key={section.category ?? '-'} href={`#section-${index}`} className={styles.categoryLink}>
              {section.category ?? t('otherCategory')}
            </a>
          ))}
        </nav>
      )}

      {data.dishes.length === 0 && <EmptyState title={t('menu')} />}

      {recommended.length > 0 && (
        <section className={styles.section} aria-labelledby="section-chef">
          <h2 id="section-chef" className={styles.sectionTitle}>
            {t('chefRecommends')}
          </h2>
          <p className={styles.sectionHint}>{t('chefRecommendsHint')}</p>
          <ul className={styles.dishes}>
            {recommended.map((dish) => (
              <DishRow key={dish.id} dish={dish} menu={data} cart={cart} onQuantity={setQuantity} onAdd={addLine} onCustomize={setCustomizing} />
            ))}
          </ul>
        </section>
      )}

      {sections.map((section, index) => (
        <section key={section.category ?? '-'} id={`section-${index}`} className={styles.section} aria-labelledby={`section-${index}-title`}>
          <h2 id={`section-${index}-title`} className={styles.sectionTitle}>
            {section.category ?? t('otherCategory')}
          </h2>
          <ul className={styles.dishes}>
            {section.dishes.map((dish) => (
              <DishRow key={dish.id} dish={dish} menu={data} cart={cart} onQuantity={setQuantity} onAdd={addLine} onCustomize={setCustomizing} />
            ))}
          </ul>
        </section>
      ))}

      {lines.length > 0 && (
        <div className={styles.cartBar}>
          <Button
            size="lg"
            block
            icon={<ShoppingBag strokeWidth={1.8} />}
            onClick={() => setCartOpen(true)}
            aria-label={t('cart.buttonLabel', { count: cartCount(lines), total: f.money(cartTotal(lines)) })}
          >
            {t('cart.button', { count: cartCount(lines), total: f.money(cartTotal(lines)) })}
          </Button>
        </div>
      )}

      <Sheet open={customizing !== null} onClose={() => setCustomizing(null)} title={customizing?.dish.name ?? ''} subtitle={t('customizeSubtitle')}>
        {customizing && (
          <DishCustomizer
            key={customizing.dish.id}
            dish={toAvailability(customizing.dish)}
            initial={customizing.initial}
            onAdd={(lineId, quantity) => {
              addLine(lineId, quantity)
              setCustomizing(null)
            }}
          />
        )}
      </Sheet>

      <CheckoutSheet
        open={cartOpen}
        onClose={() => setCartOpen(false)}
        menuKey={key}
        menu={data}
        cart={cart}
        table={table}
        onQuantity={setQuantity}
        onPlaced={() => setCart(new Map())}
        onStale={() => void menu.refetch()}
      />
    </GuestShell>
  )
}

function DishRow({
  dish,
  menu,
  cart,
  onQuantity,
  onAdd,
  onCustomize,
}: {
  dish: GuestDish
  menu: GuestMenu
  cart: Cart
  onQuantity: (lineId: string, quantity: number) => void
  onAdd: (lineId: string) => void
  onCustomize: (value: { dish: GuestDish; initial: LineChange[] }) => void
}) {
  const { t } = useTranslation(['guest', 'orders'])
  const f = useFormat()
  const quantity = cart.get(dish.id) ?? 0
  const suggested = suggestedChanges(dish)
  const suggestedText = suggested.length > 0 ? changeText(dish, suggested, t) : ''
  const canOrder = menu.ordering && dish.state !== 'Unavailable'

  return (
    <li className={styles.dish} data-state={dish.state}>
      <div className={styles.dishMain}>
        <div className={styles.dishHead}>
          <h3 className={styles.dishName}>{dish.name}</h3>
          <span className={styles.price}>{f.money(dish.price)}</span>
        </div>
        {dish.ingredients.length > 0 && (
          <p className={styles.composition}>{t('composition', { list: dish.ingredients.map((i) => i.name.toLowerCase()).join(', ') })}</p>
        )}
        {dish.state === 'Unavailable' && (
          <p className={styles.unavailable}>
            <span className={styles.square} aria-hidden="true" />
            {t('unavailable')}
          </p>
        )}
        {dish.state === 'WithChanges' && <p className={styles.withChanges}>{t('withChanges', { changes: suggestedText })}</p>}
      </div>
      {canOrder && (
        <div className={styles.dishActions}>
          {dish.state === 'WithChanges' ? (
            <Button variant="secondary" onClick={() => onAdd(lineKey(dish.id, suggested))} aria-label={t('addWithChangesLabel', { dish: dish.name, changes: suggestedText })}>
              {t('addWithChanges')}
            </Button>
          ) : quantity > 0 ? (
            <Stepper value={quantity} label={dish.name} max={menu.maxPortions} onChange={(q) => onQuantity(dish.id, q)} />
          ) : (
            <Button variant="secondary" onClick={() => onAdd(dish.id)} aria-label={t('addLabel', { dish: dish.name })}>
              {t('add')}
            </Button>
          )}
          {isGuestCustomizable(dish) && (
            <button
              type="button"
              className={styles.customize}
              onClick={() => onCustomize({ dish, initial: dish.state === 'WithChanges' ? suggested : [] })}
              aria-label={t('customizeLabel', { dish: dish.name })}
            >
              {t('customize')}
            </button>
          )}
        </div>
      )}
    </li>
  )
}

function CheckoutSheet({
  open,
  onClose,
  menuKey,
  menu,
  cart,
  table,
  onQuantity,
  onPlaced,
  onStale,
}: {
  open: boolean
  onClose: () => void
  menuKey: string
  menu: GuestMenu
  cart: Cart
  /** Стол из QR; null — гость выбирает сам (общий QR). */
  table: number | null
  onQuantity: (lineId: string, quantity: number) => void
  onPlaced: () => void
  onStale: () => void
}) {
  const { t } = useTranslation(['guest', 'orders'])
  const f = useFormat()
  const navigate = useNavigate()
  const place = usePlaceGuestOrder(menuKey)
  const [step, setStep] = useState<'cart' | 'sbp'>('cart')
  const [payment, setPayment] = useState<PaymentMethod>('Sbp')
  const [chosenTable, setChosenTable] = useState<string>('')
  const [error, setError] = useState<string | null>(null)
  // Один ключ на попытку с этим составом: повторное нажатие или обрыв связи не создадут второй заказ.
  const attemptKey = useMemo(() => (cart.size > 0 ? uuid() : ''), [cart])

  const lines = cartLines(cart, menu)
  const count = cartCount(lines)
  const total = cartTotal(lines)
  const tooMany = count > menu.maxPortions
  const blocked = lines.some(lineBlocked)
  const finalTable = table ?? (chosenTable ? Number(chosenTable) : null)

  const close = () => {
    setStep('cart')
    setError(null)
    onClose()
  }

  const submit = () => {
    setError(null)
    place.mutate(
      { order: { items: orderItems(new Map(lines.map((l) => [l.key, l.quantity]))), table: finalTable, payment }, idempotencyKey: attemptKey },
      {
        onSuccess: (order) => {
          rememberOrder(menuKey, order.token)
          onPlaced()
          close()
          void navigate(`/m/${menuKey}/order/${order.token}`)
        },
        onError: (cause) => {
          setStep('cart')
          setError(guestErrorText(cause, t))
          if (cause instanceof ApiError && cause.code === 'guest.unavailable') onStale()
        },
      },
    )
  }

  const footer =
    step === 'cart' ? (
      <Button
        size="lg"
        block
        disabled={lines.length === 0 || tooMany || blocked || place.isPending}
        onClick={() => (payment === 'Sbp' ? setStep('sbp') : submit())}
      >
        {payment === 'Sbp' ? t('place', { total: f.money(total) }) : t('placeCash', { total: f.money(total) })}
      </Button>
    ) : (
      <div className={styles.sbpActions}>
        <Button variant="text" onClick={() => setStep('cart')}>
          {t('sbp.back')}
        </Button>
        <Button size="lg" disabled={place.isPending} onClick={submit}>
          {t('sbp.paid')}
        </Button>
      </div>
    )

  return (
    <Sheet open={open} onClose={close} title={step === 'cart' ? t('cart.title') : t('sbp.title')} footer={footer}>
      {step === 'sbp' ? (
        <div className={styles.sbp}>
          <QrCode value={`mise-demo-sbp:${menuKey}:${total}`} label={t('sbp.qrAlt')} size={220} className={styles.sbpQr} />
          <p className={styles.sbpAmount}>
            {t('sbp.amount')} <strong>{f.money(total)}</strong>
          </p>
          <p className={styles.sbpHint}>{t('sbp.scan')}</p>
          <p className={styles.sbpDemo}>{t('sbp.demo')}</p>
        </div>
      ) : lines.length === 0 ? (
        <EmptyState>{t('cart.empty')}</EmptyState>
      ) : (
        <div className={styles.checkout}>
          <ul className={styles.lines}>
            {lines.map((line) => {
              const changes = changeText(line.dish, line.changes, t)
              return (
                <li key={line.key} className={styles.line}>
                  <span className={styles.lineName}>
                    {line.dish.name}
                    {changes && <span className={styles.lineChanges}>{changes}</span>}
                    {lineBlocked(line) && <span className={styles.lineBlocked}>{t('cart.blocked')}</span>}
                  </span>
                  <span className={styles.lineAmount}>{f.money(line.dish.price * line.quantity)}</span>
                  <Stepper
                    value={line.quantity}
                    label={changes ? `${line.dish.name} (${changes})` : line.dish.name}
                    max={menu.maxPortions}
                    onChange={(q) => onQuantity(line.key, q)}
                  />
                </li>
              )
            })}
          </ul>
          <p className={styles.total}>
            {t('cart.total')} <strong>{f.money(total)}</strong>
          </p>
          {tooMany && (
            <p className={styles.error} role="alert">
              {t('cart.max', { count: menu.maxPortions })}
            </p>
          )}

          {table !== null ? (
            <p className={styles.tableNote}>{t('table', { table })}</p>
          ) : menu.tables > 0 ? (
            <Select
              label={t('tableField')}
              value={chosenTable}
              onChange={(e) => setChosenTable(e.target.value)}
              options={[
                { value: '', label: t('noTable') },
                ...Array.from({ length: menu.tables }, (_, i) => ({ value: String(i + 1), label: t('table', { table: i + 1 }) })),
              ]}
            />
          ) : null}

          <fieldset className={styles.payment}>
            <legend className={styles.paymentTitle}>{t('payment.title')}</legend>
            {(['Sbp', 'Cash'] as const).map((method) => (
              <label key={method} className={styles.paymentOption}>
                <input type="radio" name="payment" value={method} checked={payment === method} onChange={() => setPayment(method)} />
                <span>
                  <span className={styles.paymentName}>{t(`payment.${method}`)}</span>
                  <span className={styles.paymentHint}>{t(`payment.${method}Hint`)}</span>
                </span>
              </label>
            ))}
          </fieldset>

          {error && (
            <p className={styles.error} role="alert">
              {error}
            </p>
          )}
        </div>
      )}
    </Sheet>
  )
}

function guestErrorText(cause: unknown, t: Translate) {
  if (!(cause instanceof ApiError)) return t('errors.unknown')
  if (cause.status === 429) return t('errors.rateLimited')
  switch (cause.code) {
    case 'guest.unavailable': {
      const dishes = (cause.problem?.dishes as string[] | undefined) ?? []
      return dishes.length > 0 ? t('errors.unavailable', { dishes: dishes.join(', ') }) : t('errors.unavailableGeneric')
    }
    case 'order.dish_unavailable':
      return t('errors.unavailableGeneric')
    case 'guest.ordering_off':
      return t('errors.orderingOff')
    case 'guest.invalid_table':
      return t('errors.invalidTable')
    default:
      return t('errors.unknown')
  }
}

import { CalendarClock, Leaf, Search } from 'lucide-react'
import { useId, useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { usePlanEvent, type EventPlan } from '@/api/planning'
import { useMenu } from '@/api/queries'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { ScrollArea } from '@/components/ScrollArea'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import { useOnline } from '@/pwa/network'
import { defaultEventTime, futureIso, portionsCount, toEventItems } from './planning'
import styles from './PlanningPage.module.scss'

/** «Что если»: банкет или большой заказ — хватит ли продуктов к его началу, что докупить, что спасётся, что уйдёт в стоп. */
export function PlanningPage() {
  const { t } = useTranslation('planning')
  const errorText = useErrorText()
  const online = useOnline()
  const menu = useMenu()
  const mutation = usePlanEvent()
  const [portions, setPortions] = useState<Record<string, string>>({})
  const [at, setAt] = useState(defaultEventTime)
  const [search, setSearch] = useState('')
  const [error, setError] = useState<string | null>(null)
  const searchId = useId()

  const dishes = useMemo(() => (menu.data ?? []).filter((d) => d.isActive), [menu.data])
  const needle = search.trim().toLocaleLowerCase()
  const shown = dishes.filter((d) => !needle || d.dishName.toLocaleLowerCase().includes(needle))
  const items = toEventItems(portions)
  const total = portionsCount(items)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    if (items.length === 0) return setError(t('errors.empty'))
    const when = futureIso(at)
    if (!when) return setError(t('errors.past'))
    mutation.mutate({ items, at: when }, { onError: (cause) => setError(errorText(cause)) })
  }

  return (
    <Page title={t('title')} subtitle={t('subtitle')}>
      <div className={styles.layout}>
        <Panel title={t('form.title')} subtitle={t('form.subtitle')}>
          <form className={styles.form} onSubmit={submit} noValidate>
            <Field label={t('form.at')} type="datetime-local" value={at} onChange={(e) => setAt(e.target.value)} />

            <div className={styles.search}>
              <label htmlFor={searchId} className="visually-hidden">
                {t('form.search')}
              </label>
              <Search aria-hidden="true" strokeWidth={1.8} />
              <input id={searchId} type="search" placeholder={t('form.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>

            {menu.isPending ? (
              <LoadingState lines={5} />
            ) : menu.isError ? (
              <ErrorState error={menu.error} onRetry={() => void menu.refetch()} />
            ) : (
              <ScrollArea label={t('form.dishes')} size="half">
                <ul className={styles.dishes} aria-label={t('form.dishes')}>
                  {shown.map((dish) => (
                    <li key={dish.dishId} className={styles.dish}>
                      <span className={styles.dishName}>
                        {dish.dishName}
                        <span className={styles.dishMeta}>{dish.category}</span>
                      </span>
                      <Field
                        label={t('form.portions', { dish: dish.dishName })}
                        hideLabel
                        className={styles.portions}
                        inputMode="numeric"
                        placeholder="0"
                        value={portions[dish.dishId] ?? ''}
                        onChange={(e) => setPortions((current) => ({ ...current, [dish.dishId]: e.target.value.replace(/\D/g, '').slice(0, 3) }))}
                      />
                    </li>
                  ))}
                </ul>
              </ScrollArea>
            )}

            {error && (
              <p className={styles.error} role="alert">
                {error}
              </p>
            )}

            <div className={styles.submit}>
              <span className={styles.total}>{t('form.total', { dishes: items.length, portions: total })}</span>
              <Button type="submit" icon={<CalendarClock aria-hidden="true" strokeWidth={1.8} />} disabled={mutation.isPending || !online}>
                {mutation.isPending ? t('form.calculating') : t('form.calculate')}
              </Button>
            </div>
          </form>
        </Panel>

        <Panel title={t('result.title')}>
          {mutation.data ? <PlanResult plan={mutation.data} /> : <EmptyState title={t('result.emptyTitle')}>{t('result.emptyHint')}</EmptyState>}
        </Panel>
      </div>
    </Page>
  )
}

function PlanResult({ plan }: { plan: EventPlan }) {
  const { t } = useTranslation('planning')
  const f = useFormat()
  const short = plan.lines.filter((l) => l.shortage > 0)
  const rescued = plan.lines.filter((l) => l.rescuedQty > 0)
  const eventTime = f.moment(plan.at)

  return (
    <div className={styles.result}>
      <div className={`${styles.verdict} ${plan.feasible ? styles.ok : styles.bad}`}>
        <StatusMark tone={plan.feasible ? 'fresh' : 'crit'}>
          {plan.feasible ? t('result.feasible') : t('result.short', { count: short.length })}
        </StatusMark>
        <p className={styles.verdictNote}>
          {t('result.summary', { when: eventTime, portions: portionsCount(plan.dishes), revenue: f.money(plan.revenue) })}
        </p>
      </div>

      {plan.rescuedCost > 0 && (
        <div className={styles.rescued}>
          <p className={styles.rescuedTitle}>
            <Leaf aria-hidden="true" strokeWidth={1.8} />
            {t('result.rescued', { cost: f.money(plan.rescuedCost) })}
          </p>
          <p className={styles.rescuedList}>
            {rescued.map((l) => t('result.rescuedItem', { product: l.productName, qty: f.qty(l.rescuedQty, l.unit) })).join(' · ')}
          </p>
        </div>
      )}

      <h3 className={styles.sectionTitle}>{t('result.products')}</h3>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">{t('table.product')}</th>
              <th scope="col">{t('table.need')}</th>
              <th scope="col">{t('table.available', { when: f.time(plan.at) })}</th>
              <th scope="col">{t('table.buy')}</th>
            </tr>
          </thead>
          <tbody>
            {plan.lines.map((line) => (
              <tr key={line.productId} className={line.shortage > 0 ? styles.shortRow : undefined}>
                <th scope="row">
                  {line.productName}
                  {line.shortage > 0 && line.supplier && <span className={styles.supplier}>{line.supplier}</span>}
                </th>
                <td>{f.qty(line.need, line.unit)}</td>
                <td>{f.qty(line.availableAtEvent, line.unit)}</td>
                <td>
                  {line.shortage > 0 ? (
                    <StatusMark tone="crit" weight="regular">
                      {f.qtyExact(line.buyQty, line.unit)}
                    </StatusMark>
                  ) : (
                    <span className={styles.enough}>{t('table.enough')}</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <h3 className={styles.sectionTitle}>{t('result.stops')}</h3>
      {plan.stopsAfter.length === 0 ? (
        <p className={styles.note}>{t('result.noStops')}</p>
      ) : (
        <ul className={styles.stops}>
          {plan.stopsAfter.map((stop) => (
            <li key={stop.dishId}>
              <StatusMark tone="warn" weight="regular">
                {stop.name}
              </StatusMark>
              <span className={styles.note}>{t('result.stopBefore', { portions: f.portionsEnoughFor(stop.portionsBefore) })}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

import { useQuery } from '@tanstack/react-query'
import { useMemo, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from '@/api/client'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { Segmented } from '@/components/Segmented'
import { Select } from '@/components/Select'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { useFormat } from '@/lib/useFormat'
import {
  consumptionByDay,
  daysInPeriod,
  periodRange,
  stopCauses,
  worstRates,
  writeOffDays,
  type ConsumptionReport,
  type DeficitItem,
  type KpiReport,
  type ReportPeriod,
  type WriteOffReport,
} from './reportData'
import styles from './ReportsPage.module.scss'

const PERIODS: ReportPeriod[] = ['today', 'week', 'month']
const TOP = 8

/** Цвета графиков — токены темы: при смене «Светлая/Графит» графики перекрашиваются сами (DESIGN.md §6.3: ≤ 3 цветов). */
const COLOR = { main: 'var(--brand)', expiry: 'var(--expired-bar)', manual: 'var(--warn-bar)', grid: 'var(--hairline)', axis: 'var(--muted)' }
const AXIS = { stroke: COLOR.grid, tick: { fill: COLOR.axis, fontSize: 12 }, tickLine: false } as const

/** Отчёты для менеджера: технические числа здесь допустимы, но каждое — с подписью словами. */
export function ReportsPage() {
  const { t } = useTranslation('reports')
  const [period, setPeriod] = useState<ReportPeriod>('week')
  // Начало периода фиксируется при выборе: ключ запроса не меняется на каждой отрисовке.
  const range = useMemo(() => periodRange(period, new Date()), [period])

  return (
    <Page title={t('title')} subtitle={t('subtitle')}>
      <Segmented label={t('period.label')} value={period} options={PERIODS.map((p) => ({ value: p, label: t(`period.${p}`) }))} onChange={setPeriod} />
      <Kpi from={range.from} />
      <div className={styles.grid}>
        <WriteOffRates from={range.from} />
        <StopCauses from={range.from} />
        <WriteOffsByDay from={range.from} />
        <Consumption from={range.from} bucket={range.bucket} />
      </div>
    </Page>
  )
}

function useReport<T>(name: string, from: string, extra = '') {
  return useQuery({
    queryKey: ['analytics', name, from, extra],
    queryFn: ({ signal }) => api.get<T>(`/api/v1/analytics/${name}?from=${encodeURIComponent(from)}${extra}`, { signal }),
    placeholderData: (previous) => previous,
  })
}

function Kpi({ from }: { from: string }) {
  const { t } = useTranslation('reports')
  const f = useFormat()
  const query = useReport<KpiReport>('kpi', from)
  if (query.isPending) return <LoadingState lines={2} />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  const p = query.data.period
  const cells = [
    { value: f.number(p.orders, 0), text: t('kpi.orders', { count: p.orders }), note: t('kpi.cancelled', { count: p.cancelledOrders }) },
    { value: f.money(p.revenue), text: t('kpi.revenue'), note: t('kpi.ticket', { ticket: f.money(p.averageTicket) }) },
    { value: `${f.number(p.writeOffRate * 100)} %`, text: t('kpi.writeOff'), note: t('kpi.writeOffNote') },
    { value: f.number(p.averageDishesInStop), text: t('kpi.stop'), note: t('kpi.stopNote', { hours: f.number(p.dishMinutesInStop / 60, 0) }) },
  ]
  return (
    <section className={styles.kpi} aria-label={t('kpi.label')}>
      {cells.map((cell) => (
        <div key={cell.text} className={styles.kpiCell}>
          <span className={styles.kpiValue}>{cell.value}</span>
          <span className={styles.kpiText}>{cell.text}</span>
          <span className={styles.kpiNote}>{cell.note}</span>
        </div>
      ))}
    </section>
  )
}

interface ChartPanelProps {
  title: string
  subtitle: string
  state: { isPending: boolean; isError: boolean; error: unknown; refetch: () => unknown }
  empty: boolean
  emptyText: string
  /** Те же данные таблицей — для тех, кто не видит график (DESIGN.md §11). */
  table: { head: string[]; rows: (string | number)[][] }
  action?: ReactNode
  children: ReactNode
}

function ChartPanel({ title, subtitle, state, empty, emptyText, table, action, children }: ChartPanelProps) {
  const { t } = useTranslation('reports')
  return (
    <Panel title={title} subtitle={subtitle} action={action}>
      {state.isPending ? (
        <LoadingState lines={4} />
      ) : state.isError ? (
        <ErrorState error={state.error} onRetry={() => void state.refetch()} />
      ) : empty ? (
        <EmptyState title={emptyText} />
      ) : (
        <figure className={styles.figure}>
          <div className={styles.chart} aria-hidden="true">
            {children}
          </div>
          <details className={styles.table}>
            <summary>{t('asTable')}</summary>
            <table>
              <thead>
                <tr>
                  {table.head.map((h) => (
                    <th key={h} scope="col">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {table.rows.map((row) => (
                  <tr key={String(row[0])}>
                    {row.map((cell, i) => (i === 0 ? <th key={i} scope="row">{cell}</th> : <td key={i}>{cell}</td>))}
                  </tr>
                ))}
              </tbody>
            </table>
          </details>
        </figure>
      )}
    </Panel>
  )
}

/** Подсказка над точкой графика — в стиле панелей: граница, без тени. */
interface TooltipProps {
  active?: boolean
  payload?: readonly { dataKey?: unknown; value?: unknown; name?: unknown; color?: string }[]
  label?: unknown
  format: (value: number, name: string) => string
}

function ChartTooltip({ active, payload, label, format }: TooltipProps) {
  if (!active || !payload?.length) return null
  return (
    <div className={styles.tooltip}>
      <p className={styles.tooltipLabel}>{String(label ?? '')}</p>
      {payload.map((entry) => (
        <p key={String(entry.dataKey)} className={styles.tooltipRow}>
          <span className={styles.tooltipSquare} style={{ background: entry.color }} aria-hidden="true" />
          {format(Number(entry.value), String(entry.name))}
        </p>
      ))}
    </div>
  )
}

function WriteOffRates({ from }: { from: string }) {
  const { t } = useTranslation('reports')
  const f = useFormat()
  const query = useReport<KpiReport>('kpi', from)
  const data = worstRates(query.data?.period.worstWriteOffRates ?? [], TOP)
  return (
    <ChartPanel
      title={t('rates.title')}
      subtitle={t('rates.subtitle')}
      state={query}
      empty={data.length === 0}
      emptyText={t('rates.empty')}
      table={{
        head: [t('table.product'), t('rates.share'), t('rates.writtenOff'), t('rates.received')],
        rows: data.map((d) => [d.name, `${f.number(d.percent)} %`, f.qty(d.item.writtenOffQty, d.item.unit), f.qty(d.item.receivedQty, d.item.unit)]),
      }}
    >
      <ResponsiveContainer width="100%" height={Math.max(160, data.length * 36 + 40)}>
        <BarChart data={data} layout="vertical" margin={{ top: 4, right: 24, bottom: 4, left: 8 }}>
          <CartesianGrid horizontal={false} stroke={COLOR.grid} />
          <XAxis type="number" unit=" %" {...AXIS} />
          <YAxis type="category" dataKey="name" width={124} {...AXIS} />
          <Tooltip
            cursor={{ fill: 'var(--bg)' }}
            content={(props) => (
              <ChartTooltip
                active={props.active}
                payload={props.payload}
                label={props.label}
                format={(value) => {
                  const item = data.find((d) => d.name === props.label)?.item
                  return item ? t('rates.tooltip', { percent: f.number(value), qty: f.qty(item.writtenOffQty, item.unit) }) : `${value} %`
                }}
              />
            )}
          />
          <Bar dataKey="percent" name={t('rates.share')} fill={COLOR.expiry} barSize={18} />
        </BarChart>
      </ResponsiveContainer>
    </ChartPanel>
  )
}

function StopCauses({ from }: { from: string }) {
  const { t } = useTranslation('reports')
  const f = useFormat()
  const query = useReport<{ items: DeficitItem[] }>('deficit', from)
  const data = stopCauses(query.data?.items ?? [], TOP)
  return (
    <ChartPanel
      title={t('stop.title')}
      subtitle={t('stop.subtitle')}
      state={query}
      empty={data.length === 0}
      emptyText={t('stop.empty')}
      table={{
        head: [t('table.product'), t('stop.hours'), t('stop.events'), t('stop.dishes')],
        rows: data.map((d) => [d.name, f.number(d.hours), d.item.stopEvents, d.item.dishesAffected]),
      }}
    >
      <ResponsiveContainer width="100%" height={Math.max(160, data.length * 36 + 40)}>
        <BarChart data={data} layout="vertical" margin={{ top: 4, right: 24, bottom: 4, left: 8 }}>
          <CartesianGrid horizontal={false} stroke={COLOR.grid} />
          <XAxis type="number" {...AXIS} />
          <YAxis type="category" dataKey="name" width={124} {...AXIS} />
          <Tooltip
            cursor={{ fill: 'var(--bg)' }}
            content={(props) => {
              const item = data.find((d) => d.name === props.label)?.item
              return <ChartTooltip active={props.active} payload={props.payload} label={props.label} format={(value) => t('stop.tooltip', { hours: f.number(value), count: item?.stopEvents ?? 0, dishes: item?.dishesAffected ?? 0 })} />
            }}
          />
          <Bar dataKey="hours" name={t('stop.hours')} fill={COLOR.main} barSize={18} />
        </BarChart>
      </ResponsiveContainer>
    </ChartPanel>
  )
}

function WriteOffsByDay({ from }: { from: string }) {
  const { t } = useTranslation('reports')
  const f = useFormat()
  const query = useReport<WriteOffReport>('writeoffs', from)
  const days = useMemo(() => daysInPeriod(from, new Date()), [from])
  const data = writeOffDays(query.data?.byDay ?? [], days).map((d) => ({ ...d, label: f.date(`${d.date}T12:00:00+03:00`) }))
  const total = (query.data?.expiryOperations ?? 0) + (query.data?.manualOperations ?? 0)
  return (
    <ChartPanel
      title={t('writeoffs.title')}
      subtitle={t('writeoffs.subtitle')}
      state={query}
      empty={total === 0}
      emptyText={t('writeoffs.empty')}
      table={{ head: [t('table.day'), t('writeoffs.expiry'), t('writeoffs.manual')], rows: data.map((d) => [d.label, d.expiry, d.manual]) }}
    >
      <ResponsiveContainer width="100%" height={240}>
        <BarChart data={data} margin={{ top: 8, right: 32, bottom: 4, left: -16 }}>
          <CartesianGrid vertical={false} stroke={COLOR.grid} />
          <XAxis dataKey="label" {...AXIS} interval="preserveStartEnd" minTickGap={16} />
          <YAxis allowDecimals={false} {...AXIS} />
          <Tooltip cursor={{ fill: 'var(--bg)' }} content={(props) => <ChartTooltip active={props.active} payload={props.payload} label={props.label} format={(value, name) => `${name}: ${value}`} />} />
          <Legend iconType="square" iconSize={8} wrapperStyle={{ fontSize: 13, color: 'var(--ink-2)' }} />
          <Bar dataKey="expiry" name={t('writeoffs.expiry')} stackId="w" fill={COLOR.expiry} maxBarSize={28} />
          <Bar dataKey="manual" name={t('writeoffs.manual')} stackId="w" fill={COLOR.manual} maxBarSize={28} />
        </BarChart>
      </ResponsiveContainer>
    </ChartPanel>
  )
}

function Consumption({ from, bucket }: { from: string; bucket: 'Hour' | 'Day' }) {
  const { t } = useTranslation('reports')
  const f = useFormat()
  const query = useReport<ConsumptionReport>('consumption', from, `&bucket=${bucket}&limit=10`)
  const series = query.data?.series ?? []
  const [chosen, setChosen] = useState<string | null>(null)
  const current = series.find((s) => s.productId === chosen) ?? series[0]
  const days = useMemo(() => daysInPeriod(from, new Date()), [from])
  const data =
    bucket === 'Hour'
      ? (current?.points ?? []).map((p) => ({ label: f.time(p.bucket), qty: p.qty }))
      : consumptionByDay(current?.points ?? [], days).map((d) => ({ label: f.date(`${d.date}T12:00:00+03:00`), qty: d.qty }))

  return (
    <ChartPanel
      title={t('consumption.title')}
      subtitle={t('consumption.subtitle')}
      state={query}
      empty={!current}
      emptyText={t('consumption.empty')}
      action={
        series.length > 1 && (
          <Select
            className={styles.productSelect}
            label={t('table.product')}
            hideLabel
            value={current?.productId ?? ''}
            options={series.map((s) => ({ value: s.productId, label: s.productName }))}
            onChange={(e) => setChosen(e.target.value)}
          />
        )
      }
      table={{ head: [t(bucket === 'Hour' ? 'table.hour' : 'table.day'), t('consumption.qty')], rows: current ? data.map((d) => [d.label, f.qty(d.qty, current.unit)]) : [] }}
    >
      {current && (
        <ResponsiveContainer width="100%" height={240}>
          <LineChart data={data} margin={{ top: 8, right: 32, bottom: 4, left: 0 }}>
            <CartesianGrid vertical={false} stroke={COLOR.grid} />
            <XAxis dataKey="label" {...AXIS} interval="preserveStartEnd" minTickGap={16} />
            <YAxis {...AXIS} width={64} tickFormatter={(value: number) => f.qty(value, current.unit)} />
            <Tooltip content={(props) => <ChartTooltip active={props.active} payload={props.payload} label={props.label} format={(value) => f.qty(value, current.unit)} />} />
            <Line type="monotone" dataKey="qty" name={current.productName} stroke={COLOR.main} strokeWidth={2} dot={{ r: 2.5, fill: COLOR.main }} activeDot={{ r: 4 }} />
          </LineChart>
        </ResponsiveContainer>
      )}
    </ChartPanel>
  )
}

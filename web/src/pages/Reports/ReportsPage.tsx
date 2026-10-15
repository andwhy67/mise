import { useQuery } from '@tanstack/react-query'
import { ArrowDownRight, ArrowUpRight, Download, Minus, Printer } from 'lucide-react'
import { useMemo, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from '@/api/client'
import { Button } from '@/components/Button'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { Segmented } from '@/components/Segmented'
import { Combobox } from '@/components/Combobox'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { download, toCsv, type CsvSection } from '@/lib/csv'
import { useFormat } from '@/lib/useFormat'
import {
  compare,
  comparePoints,
  consumptionByDay,
  daysInPeriod,
  deficitRanking,
  marginShare,
  moneyRanking,
  periodRange,
  previousRange,
  worstRates,
  writeOffDays,
  type Comparison,
  type ConsumptionReport,
  type DeficitItem,
  type KpiReport,
  type MoneyReport,
  type ReportPeriod,
  type WriteOffReport,
} from './reportData'
import styles from './ReportsPage.module.scss'

const PERIODS: ReportPeriod[] = ['today', 'week', 'month']
const TOP = 8

/** Цвета графиков — токены темы: при смене «Светлая/Графит» графики перекрашиваются сами (DESIGN.md §6.3: ≤ 3 цветов). */
const COLOR = { main: 'var(--brand)', expiry: 'var(--expired-bar)', manual: 'var(--warn-bar)', fresh: 'var(--fresh-bar)', grid: 'var(--hairline)', axis: 'var(--muted)' }
const AXIS = { stroke: COLOR.grid, tick: { fill: COLOR.axis, fontSize: 12 }, tickLine: false } as const

/** Отчёты для менеджера: технические числа здесь допустимы, но каждое — с подписью словами. */
export function ReportsPage() {
  const { t } = useTranslation('reports')
  const [period, setPeriod] = useState<ReportPeriod>('week')
  // Начало периода фиксируется при выборе: ключ запроса не меняется на каждой отрисовке.
  const range = useMemo(() => {
    const now = new Date()
    const current = periodRange(period, now)
    return { ...current, previous: previousRange(period, current.from, now) }
  }, [period])
  const previous: Previous = { ...range.previous, label: t(`compare.vs.${period}`) }

  return (
    <Page
      title={t('title')}
      subtitle={t('subtitle')}
      actions={<ExportActions from={range.from} bucket={range.bucket} periodLabel={t(`period.${period}`)} />}
    >
      <Segmented label={t('period.label')} value={period} options={PERIODS.map((p) => ({ value: p, label: t(`period.${p}`) }))} onChange={setPeriod} />
      {/* На бумаге переключателя нет — период пишем словами. */}
      <p className={styles.printPeriod}>{t('print.period', { period: t(`period.${period}`) })}</p>
      <Kpi from={range.from} previous={previous} />
      <Money from={range.from} previous={previous} />
      <div className={styles.grid}>
        <MoneyByProduct from={range.from} />
        <WriteOffRates from={range.from} />
        <Deficit from={range.from} />
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

/** Прошлый период той же длины — для «▲ 12 % к прошлой неделе». */
interface Previous {
  from: string
  to: string
  label: string
}

const toParam = (to: string) => `&to=${encodeURIComponent(to)}`

/** Изменение к прошлому периоду: стрелка + слова; цвет — по смыслу (рост потерь — плохо), а не по знаку. */
function Delta({ comparison, text }: { comparison: Comparison | null; text: string }) {
  if (!comparison) return null
  const Icon = comparison.direction === 'up' ? ArrowUpRight : comparison.direction === 'down' ? ArrowDownRight : Minus
  return (
    <span className={styles.delta} data-tone={comparison.tone}>
      <Icon aria-hidden="true" strokeWidth={2} />
      {text}
    </span>
  )
}

/** «+20 % к прошлой неделе», «без изменений», «раньше не было». Оба нуля — сравнивать нечего. */
function useDeltaText() {
  const { t } = useTranslation('reports')
  const f = useFormat()
  return (c: Comparison & { points?: number }, label: string) => {
    if (c.direction === 'flat') return t('compare.flat', { vs: label })
    if (c.points !== undefined) return t('compare.points', { value: `${c.points > 0 ? '+' : '−'}${f.number(Math.abs(c.points))}`, vs: label })
    if (c.percent === null) return t('compare.new', { vs: label })
    return t('compare.percent', { value: `${c.percent > 0 ? '+' : '−'}${f.number(Math.abs(c.percent), 0)}`, vs: label })
  }
}

function Kpi({ from, previous }: { from: string; previous: Previous }) {
  const { t } = useTranslation('reports')
  const f = useFormat()
  const query = useReport<KpiReport>('kpi', from)
  const before = useReport<KpiReport>('kpi', previous.from, toParam(previous.to))
  const deltaText = useDeltaText()
  if (query.isPending) return <LoadingState lines={2} />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  const p = query.data.period
  const b = before.data?.period
  const delta = (current: number, old: number | undefined, better: 'up' | 'down', points = false) => {
    if (old === undefined || (current === 0 && old === 0)) return null
    const c = points ? comparePoints(current, old, better) : compare(current, old, better)
    return <Delta comparison={c} text={deltaText(c, previous.label)} />
  }
  const cells = [
    { value: f.number(p.orders, 0), text: t('kpi.orders', { count: p.orders }), note: t('kpi.cancelled', { count: p.cancelledOrders }), delta: delta(p.orders, b?.orders, 'up') },
    { value: f.money(p.revenue), text: t('kpi.revenue'), note: t('kpi.ticket', { ticket: f.money(p.averageTicket) }), delta: delta(p.revenue, b?.revenue, 'up') },
    {
      value: `${f.number(p.writeOffRate * 100)} %`,
      text: t('kpi.writeOff'),
      note: t('kpi.writeOffNote'),
      delta: delta(p.writeOffRate, b?.writeOffRate, 'down', true),
    },
    {
      value: f.number(p.averageDishesInStop),
      text: t('kpi.stop'),
      note: t('kpi.stopNote', { hours: f.number(p.dishMinutesInStop / 60, 0) }),
      delta: delta(p.averageDishesInStop, b?.averageDishesInStop, 'down'),
    },
  ]
  return (
    <section className={styles.kpi} aria-label={t('kpi.label')}>
      {cells.map((cell) => (
        <div key={cell.text} className={styles.kpiCell}>
          <span className={styles.kpiValue}>{cell.value}</span>
          <span className={styles.kpiText}>{cell.text}</span>
          <span className={styles.kpiNote}>{cell.note}</span>
          {cell.delta}
        </div>
      ))}
    </section>
  )
}

/** «Деньги»: сколько заработали на проданном, сколько потеряли на списаниях и сколько спасли продажей «горящего». */
function Money({ from, previous }: { from: string; previous: Previous }) {
  const { t } = useTranslation('reports')
  const f = useFormat()
  const query = useReport<MoneyReport>('money', from)
  const before = useReport<MoneyReport>('money', previous.from, toParam(previous.to))
  const deltaText = useDeltaText()
  if (query.isPending) return <LoadingState lines={2} />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  const m = query.data
  const b = before.data
  const share = marginShare(m)
  // Себестоимость и закупки — нейтрально: растут вместе с продажами, это не «хорошо» и не «плохо».
  const delta = (current: number, old: number | undefined, better: 'up' | 'down' | 'neutral') => {
    if (old === undefined || (current === 0 && old === 0) || (b && b.costCoverage === 0)) return null
    const c = compare(current, old, better)
    return <Delta comparison={c} text={deltaText(c, previous.label)} />
  }
  const cells = [
    // Цен нет совсем — маржа «100 %» была бы неправдой: себестоимость просто неизвестна.
    m.costCoverage === 0 && m.revenue > 0
      ? { value: '—', text: t('money.margin'), note: t('money.noPrices') }
      : {
          value: f.money(m.grossMargin),
          text: t('money.margin'),
          note: share === null ? t('money.noRevenue') : t('money.marginNote', { percent: f.number(share * 100, 0) }),
          delta: delta(m.grossMargin, b?.grossMargin, 'up'),
        },
    {
      value: f.money(m.costOfSales),
      text: t('money.costOfSales'),
      note: t('money.costOfSalesNote', { revenue: f.money(m.revenue) }),
      delta: delta(m.costOfSales, b?.costOfSales, 'neutral'),
    },
    { value: f.money(m.purchases), text: t('money.purchases'), note: t('money.purchasesNote'), delta: delta(m.purchases, b?.purchases, 'neutral') },
    {
      value: f.money(m.losses),
      text: t('money.losses'),
      note: t('money.lossesNote', { expiry: f.money(m.expiryLosses), manual: f.money(m.manualLosses) }),
      tone: 'crit',
      delta: delta(m.losses, b?.losses, 'down'),
    },
    { value: f.money(m.rescued), text: t('money.rescued'), note: t('money.rescuedNote'), tone: 'fresh', delta: delta(m.rescued, b?.rescued, 'up') },
  ]
  return (
    <section className={styles.money} aria-label={t('money.title')}>
      <h2 className={styles.moneyTitle}>{t('money.title')}</h2>
      <div className={styles.moneyCells}>
        {cells.map((cell) => (
          <div key={cell.text} className={styles.kpiCell} data-tone={cell.tone}>
            <span className={styles.kpiValue}>{cell.value}</span>
            <span className={styles.kpiText}>{cell.text}</span>
            <span className={styles.kpiNote}>{cell.note}</span>
            {'delta' in cell && cell.delta}
          </div>
        ))}
      </div>
      {m.costCoverage < 1 && <p className={styles.moneyNote}>{t('money.coverage', { percent: f.number(m.costCoverage * 100, 0) })}</p>}
    </section>
  )
}

function MoneyByProduct({ from }: { from: string }) {
  const { t } = useTranslation('reports')
  const f = useFormat()
  const query = useReport<MoneyReport>('money', from)
  const data = moneyRanking(query.data?.products ?? [], TOP)
  return (
    <ChartPanel
      title={t('moneyProducts.title')}
      subtitle={t('moneyProducts.subtitle')}
      state={query}
      empty={data.length === 0}
      emptyText={t('moneyProducts.empty')}
      table={{
        head: [t('table.product'), t('moneyProducts.losses'), t('moneyProducts.rescued'), t('moneyProducts.rescuedQty'), t('moneyProducts.purchased')],
        rows: data.map((d) => [d.name, f.money(d.item.losses), f.money(d.item.rescued), f.qty(d.item.rescuedQty, d.item.unit), f.money(d.item.purchased)]),
      }}
    >
      <ResponsiveContainer width="100%" height={Math.max(180, data.length * 44 + 60)}>
        <BarChart accessibilityLayer={false} data={data} layout="vertical" margin={{ top: 4, right: 24, bottom: 4, left: 8 }}>
          <CartesianGrid horizontal={false} stroke={COLOR.grid} />
          <XAxis type="number" allowDecimals={false} {...AXIS} tickFormatter={(value: number) => f.money(value)} />
          <YAxis type="category" dataKey="name" width={124} {...AXIS} />
          <Tooltip
            cursor={{ fill: 'var(--bg)' }}
            content={(props) => (
              <ChartTooltip active={props.active} payload={props.payload} label={props.label} format={(value, name) => `${name}: ${f.money(value)}`} />
            )}
          />
          <Legend iconType="square" iconSize={8} wrapperStyle={{ fontSize: 13, color: 'var(--ink-2)' }} />
          <Bar dataKey="losses" name={t('moneyProducts.losses')} fill={COLOR.expiry} barSize={12} />
          <Bar dataKey="rescued" name={t('moneyProducts.rescued')} fill={COLOR.fresh} barSize={12} />
        </BarChart>
      </ResponsiveContainer>
    </ChartPanel>
  )
}

/**
 * Экспорт: CSV (все таблицы отчёта одним файлом — для Excel: «;», BOM) и PDF — печать страницы
 * (навигация и кнопки скрыты стилями печати, тема — всегда светлая).
 */
function ExportActions({ from, bucket, periodLabel }: { from: string; bucket: 'Hour' | 'Day'; periodLabel: string }) {
  const { t, i18n } = useTranslation('reports')
  const kpi = useReport<KpiReport>('kpi', from)
  const money = useReport<MoneyReport>('money', from)
  const deficit = useReport<{ items: DeficitItem[] }>('deficit', from)
  const writeoffs = useReport<WriteOffReport>('writeoffs', from)
  const consumption = useReport<ConsumptionReport>('consumption', from, `&bucket=${bucket}&limit=10`)
  const ready = kpi.data && money.data && deficit.data && writeoffs.data && consumption.data

  const exportCsv = () => {
    if (!ready) return
    const p = kpi.data!.period
    const m = money.data!
    const base = (unit: string) => t(`csv.unit.${unit}`)
    const sections: CsvSection[] = [
      {
        title: t('csv.summary', { period: periodLabel }),
        head: [t('csv.metric'), t('csv.value')],
        rows: [
          [t('csv.orders'), p.orders],
          [t('csv.cancelled'), p.cancelledOrders],
          [t('csv.revenue'), p.revenue],
          [t('csv.averageTicket'), p.averageTicket],
          [t('csv.writeOffRate'), Math.round(p.writeOffRate * 1000) / 10],
          [t('csv.dishesInStop'), p.averageDishesInStop],
          // Цен нет совсем — маржа неизвестна (как на экране), а не «равна выручке».
          [t('csv.margin'), m.costCoverage === 0 && m.revenue > 0 ? null : m.grossMargin],
          [t('csv.costOfSales'), m.costOfSales],
          [t('csv.purchases'), m.purchases],
          [t('csv.losses'), m.losses],
          [t('csv.rescued'), m.rescued],
        ],
      },
      {
        title: t('moneyProducts.title'),
        head: [t('table.product'), t('csv.purchasedRub'), t('csv.costOfSalesRub'), t('csv.lossesRub'), t('csv.rescuedRub'), t('csv.rescuedQty'), t('csv.unitHead')],
        rows: m.products
          .filter((x) => x.purchased || x.costOfSales || x.losses || x.rescued)
          .map((x) => [x.productName, x.purchased, x.costOfSales, x.losses, x.rescued, x.rescuedQty, base(x.unit)]),
      },
      {
        title: t('deficit.title'),
        head: [t('table.product'), t('deficit.refusals'), t('csv.shortage'), t('deficit.stops'), t('deficit.hours'), t('deficit.belowMin'), t('csv.unitHead')],
        rows: deficit.data!.items.map((x) => [
          x.productName,
          x.refusals,
          x.shortageQty,
          x.stopEvents,
          Math.round((x.dishMinutesInStop / 60) * 10) / 10,
          Math.round(x.shareBelowMinimum * 1000) / 10,
          base(x.unit),
        ]),
      },
      {
        title: t('writeoffs.title'),
        head: [t('table.product'), t('writeoffs.expiry'), t('writeoffs.manual'), t('csv.total'), t('csv.unitHead')],
        rows: writeoffs.data!.byProduct.map((x) => [x.productName, x.expiryQty, x.manualQty, x.totalQty, base(x.unit)]),
      },
      {
        title: t('consumption.title'),
        head: [t('table.product'), t('csv.total'), t('csv.unitHead')],
        rows: consumption.data!.series.map((x) => [x.productName, x.total, base(x.unit)]),
      },
    ]
    const decimal = i18n.language.startsWith('ru') ? ',' : '.'
    download(`mise-report-${from.slice(0, 10)}.csv`, toCsv(sections, decimal))
  }

  return (
    <span className={styles.exportActions}>
      <Button variant="secondary" icon={<Download />} disabled={!ready} onClick={exportCsv} aria-label={t('export.csvLabel', { period: periodLabel })}>
        {t('export.csv')}
      </Button>
      <Button variant="secondary" icon={<Printer />} onClick={() => window.print()} aria-label={t('export.pdfLabel')}>
        {t('export.pdf')}
      </Button>
    </span>
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
          {/* График — для глаз; клавиатура и диктор работают с таблицей ниже (accessibilityLayer у графиков выключен). */}
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
        <BarChart accessibilityLayer={false} data={data} layout="vertical" margin={{ top: 4, right: 24, bottom: 4, left: 8 }}>
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

/**
 * «Чего чаще всего не хватает»: сколько раз из-за продукта отказали в заказе и сколько раз он ставил блюда в стоп.
 * Оба числа — «разы», одна шкала; детали (сколько не хватило, часы в стопе, доля времени ниже минимума) — в таблице.
 */
function Deficit({ from }: { from: string }) {
  const { t } = useTranslation('reports')
  const f = useFormat()
  const query = useReport<{ items: DeficitItem[] }>('deficit', from)
  const data = deficitRanking(query.data?.items ?? [], TOP)
  return (
    <ChartPanel
      title={t('deficit.title')}
      subtitle={t('deficit.subtitle')}
      state={query}
      empty={data.length === 0}
      emptyText={t('deficit.empty')}
      table={{
        head: [t('table.product'), t('deficit.refusals'), t('deficit.shortage'), t('deficit.stops'), t('deficit.hours'), t('deficit.belowMin')],
        rows: data.map((d) => [
          d.name,
          d.refusals,
          d.item.shortageQty > 0 ? f.qty(d.item.shortageQty, d.item.unit) : '—',
          d.stops,
          f.number(d.item.dishMinutesInStop / 60),
          `${f.number(d.item.shareBelowMinimum * 100)} %`,
        ]),
      }}
    >
      <ResponsiveContainer width="100%" height={Math.max(180, data.length * 44 + 60)}>
        <BarChart accessibilityLayer={false} data={data} layout="vertical" margin={{ top: 4, right: 24, bottom: 4, left: 8 }}>
          <CartesianGrid horizontal={false} stroke={COLOR.grid} />
          <XAxis type="number" allowDecimals={false} {...AXIS} />
          <YAxis type="category" dataKey="name" width={124} {...AXIS} />
          <Tooltip
            cursor={{ fill: 'var(--bg)' }}
            content={(props) => (
              <ChartTooltip active={props.active} payload={props.payload} label={props.label} format={(value, name) => `${name}: ${f.number(value, 0)}`} />
            )}
          />
          <Legend iconType="square" iconSize={8} wrapperStyle={{ fontSize: 13, color: 'var(--ink-2)' }} />
          <Bar dataKey="refusals" name={t('deficit.refusals')} fill={COLOR.expiry} barSize={12} />
          <Bar dataKey="stops" name={t('deficit.stops')} fill={COLOR.manual} barSize={12} />
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
        <BarChart accessibilityLayer={false} data={data} margin={{ top: 8, right: 32, bottom: 4, left: -16 }}>
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
          <Combobox
            className={styles.productSelect}
            label={t('table.product')}
            hideLabel
            value={current?.productId ?? ''}
            options={series.map((s) => ({ value: s.productId, label: s.productName }))}
            onChange={setChosen}
          />
        )
      }
      table={{ head: [t(bucket === 'Hour' ? 'table.hour' : 'table.day'), t('consumption.qty')], rows: current ? data.map((d) => [d.label, f.qty(d.qty, current.unit)]) : [] }}
    >
      {current && (
        <ResponsiveContainer width="100%" height={240}>
          <LineChart accessibilityLayer={false} data={data} margin={{ top: 8, right: 32, bottom: 4, left: 0 }}>
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

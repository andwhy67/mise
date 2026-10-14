import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Pause, Play, Sprout } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { api } from '@/api/client'
import { queryKeys } from '@/api/keys'
import { Button } from '@/components/Button'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { Segmented } from '@/components/Segmented'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import styles from './SimulatorPage.module.scss'

const SCENARIOS = ['Normal', 'RushHour', 'SupplyDelay', 'Spoilage', 'FreshDelivery', 'Chaos'] as const
type Scenario = (typeof SCENARIOS)[number]
const SPEEDS = [1, 5, 10, 30, 60] as const
const COUNTERS = ['ordersCreated', 'ordersRejected', 'ordersCompleted', 'ordersCancelled', 'supplies', 'supplyItems', 'retries', 'errors'] as const

interface SimulatorState {
  control: { running: boolean; scenario: Scenario; speed: number; seed: number | null; version: number; updatedAt: string }
  online: boolean
  status: {
    running: boolean
    scenario: Scenario
    speed: number
    appliedVersion: number
    heartbeatAt: string
    startedAt: string | null
    simulatedTime: string | null
    counters: Record<(typeof COUNTERS)[number], number>
    recent: { at: string; kind: string; message: string }[]
  } | null
}

/** Симулятор (PLAN §8): поток заказов и поставок для демонстрации; управление — только у менеджера. */
export function SimulatorPage() {
  const { t } = useTranslation('simulator')
  const query = useQuery({
    queryKey: queryKeys.simulator,
    queryFn: ({ signal }) => api.get<SimulatorState>('/api/v1/simulator/state', { signal }),
    // Событие хаба обновляет сразу; опрос раз в 5 с — страховка, пока симулятор работает.
    refetchInterval: (q) => (q.state.data?.control.running ? 5_000 : false),
  })

  return (
    <Page title={t('title')} subtitle={t('subtitle')} actions={query.data && <Controls state={query.data} />}>
      {query.isPending ? (
        <LoadingState lines={6} />
      ) : query.isError ? (
        <Panel>
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </Panel>
      ) : (
        <>
          <StatusPanel state={query.data} />
          <div className={styles.columns}>
            <ScenarioPanel state={query.data} />
            <ActivityPanel state={query.data} />
          </div>
        </>
      )}
    </Page>
  )
}

function useSimulatorAction<T>(path: string, done?: (data: T) => string) {
  const client = useQueryClient()
  const errorText = useErrorText()
  return useMutation({
    mutationFn: (body?: unknown) => api.post<T>(path, body),
    onSuccess: async (data) => {
      await client.invalidateQueries({ queryKey: queryKeys.simulator })
      if (done) toast.show(done(data))
    },
    onError: (cause) => toast.show(errorText(cause), 'crit'),
  })
}

function Controls({ state }: { state: SimulatorState }) {
  const { t } = useTranslation('simulator')
  const client = useQueryClient()
  const start = useSimulatorAction('/api/v1/simulator/start', () => t('started'))
  const stop = useSimulatorAction('/api/v1/simulator/stop', () => t('stopped'))
  const seed = useSimulatorAction<{ created?: number; replayed?: number }>('/api/v1/demo/seed-stock', () => t('seeded'))
  const running = state.control.running

  return (
    <div className={styles.controls}>
      <Button
        variant="secondary"
        icon={<Sprout />}
        disabled={seed.isPending}
        onClick={() => seed.mutate(undefined, { onSuccess: () => void client.invalidateQueries() })}
      >
        {t('seed')}
      </Button>
      {running ? (
        <Button icon={<Pause />} disabled={stop.isPending} onClick={() => stop.mutate(undefined)}>
          {t('stop')}
        </Button>
      ) : (
        <Button icon={<Play />} disabled={start.isPending} onClick={() => start.mutate({})}>
          {t('start')}
        </Button>
      )}
    </div>
  )
}

function StatusPanel({ state }: { state: SimulatorState }) {
  const { t } = useTranslation('simulator')
  const f = useFormat()
  const speed = useSimulatorAction('/api/v1/simulator/speed')
  const { control, status, online } = state
  // Желаемое состояние ещё не дошло до сервиса симулятора — честно показываем «применяется».
  const pending = online && status !== null && status.appliedVersion < control.version

  let mark
  if (!online) mark = <StatusMark tone="crit">{t('status.offline')}</StatusMark>
  else if (pending) mark = <StatusMark tone="warn">{t('status.applying')}</StatusMark>
  else if (control.running) mark = <StatusMark tone="fresh">{t('status.running', { scenario: t(`scenarios.${control.scenario}.title`), speed: control.speed })}</StatusMark>
  else mark = <StatusMark tone="info">{t('status.stopped')}</StatusMark>

  return (
    <Panel>
      <div className={styles.status}>
        <div className={`${styles.statusMain} ${control.running && online ? styles.live : ''}`}>
          <div>
            {mark}
            <p className={styles.statusMeta}>
              {status?.simulatedTime ? t('status.modelTime', { when: f.moment(status.simulatedTime) }) : t('status.noModelTime')}
              {status?.heartbeatAt && ` · ${t('status.heartbeat', { time: f.time(status.heartbeatAt) })}`}
            </p>
          </div>
        </div>
        <div className={styles.speed}>
          <Segmented
            label={t('speed.label')}
            value={String(control.speed)}
            options={SPEEDS.map((s) => ({ value: String(s), label: `×${s}` }))}
            onChange={(value) => speed.mutate({ speed: Number(value) })}
          />
          <p className={styles.speedHint}>{t('speed.hint')}</p>
        </div>
      </div>
      {status && (
        <dl className={styles.counters}>
          {COUNTERS.map((key) => (
            <div key={key} className={styles.counter}>
              <dt>{t(`counters.${key}`)}</dt>
              <dd key={status.counters[key]}>{f.number(status.counters[key], 0)}</dd>
            </div>
          ))}
        </dl>
      )}
    </Panel>
  )
}

function ScenarioPanel({ state }: { state: SimulatorState }) {
  const { t } = useTranslation('simulator')
  const scenario = useSimulatorAction('/api/v1/simulator/scenario')
  return (
    <Panel title={t('scenario.title')} subtitle={t('scenario.subtitle')} titleId="sim-scenario">
      <div className={styles.scenarios} role="radiogroup" aria-labelledby="sim-scenario">
        {SCENARIOS.map((value) => (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={state.control.scenario === value}
            className={styles.scenario}
            disabled={scenario.isPending}
            onClick={() => state.control.scenario !== value && scenario.mutate({ scenario: value })}
          >
            <span className={styles.scenarioTitle}>{t(`scenarios.${value}.title`)}</span>
            <span className={styles.scenarioText}>{t(`scenarios.${value}.text`)}</span>
          </button>
        ))}
      </div>
    </Panel>
  )
}

function ActivityPanel({ state }: { state: SimulatorState }) {
  const { t } = useTranslation('simulator')
  const f = useFormat()
  const recent = state.status?.recent ?? []
  return (
    <Panel title={t('activity.title')} subtitle={t('activity.subtitle')} titleId="sim-activity">
      {recent.length === 0 ? (
        <EmptyState title={t('activity.empty')}>{t('activity.emptyHint')}</EmptyState>
      ) : (
        <ol className={styles.activity}>
          {recent.map((event) => (
            <li key={`${event.at}-${event.message}`} className={styles.event}>
              <span className={styles.eventTime}>{f.time(event.at)}</span>
              <span className={styles.eventKind}>{t(`activity.kinds.${event.kind}`, { defaultValue: event.kind })}</span>
              {/* Строка журнала симулятора — данные сервиса (номера заказов и названия блюд), не текст интерфейса. */}
              <span className={styles.eventText}>{event.message}</span>
            </li>
          ))}
        </ol>
      )}
    </Panel>
  )
}

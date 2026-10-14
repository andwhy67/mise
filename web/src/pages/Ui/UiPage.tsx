import { Plus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/Button'
import { ExpiryList } from '@/components/ExpiryList'
import { Field } from '@/components/Field'
import { HotOfferRow } from '@/components/HotOfferRow'
import { LiveIndicator } from '@/components/LiveIndicator'
import { MiseWordmark } from '@/components/MiseWordmark'
import { Panel } from '@/components/Panel'
import { ShelfLifeBar } from '@/components/ShelfLifeBar'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { StopListRow } from '@/components/StopListRow'
import { SummaryStrip } from '@/components/SummaryStrip'
import { LanguageSwitcher, ThemeSwitcher } from '@/components/Switchers'
import { ApiError } from '@/api/errors'
import { offerVerdict } from '@/lib/format'
import { useFormat, useNow } from '@/lib/useFormat'
import styles from './UiPage.module.scss'

const HOUR = 3_600_000

/** Демо всех базовых компонентов (DESIGN.md §12, п. 2): проверка тем, языков, адаптива и доступности. */
export function UiPage() {
  const { t } = useTranslation('ui')
  const f = useFormat()
  const now = useNow().getTime()
  const inHours = (hours: number) => new Date(now + hours * HOUR)

  const offers = [
    { dish: t('sample.dish1'), score: 80, reason: t('sample.reason1', { qty1: f.qty(2400, 'Gram'), when1: f.whenInline(inHours(6)), qty2: f.qty(1800, 'Milliliter'), when2: f.whenInline(inHours(20)), portions: f.portionsUpTo(16) }) },
    { dish: t('sample.dish2'), score: 54.8, reason: t('sample.reason2', { qty: f.qty(3000, 'Milliliter'), when: f.whenInline(inHours(30)), portions: f.portionsUpTo(21) }) },
    { dish: t('sample.dish3'), score: 36.4, reason: t('sample.reason3', { qty: f.qty(3200, 'Gram'), when: f.whenInline(inHours(70)) }) },
  ]

  return (
    <main className={styles.page}>
      <header className={styles.header}>
        <div>
          <h1 className={styles.h1}>{t('title')}</h1>
          <p className={styles.lead}>{t('subtitle')}</p>
          <p className={styles.lead}>{f.todayLine()}</p>
        </div>
        <div className={styles.controls}>
          <ThemeSwitcher withLabels />
          <LanguageSwitcher />
        </div>
      </header>

      <Panel title={t('sections.type')}>
        <div className={styles.typeRow}>
          <MiseWordmark size="lg" />
          <div className={styles.brandBlock}>
            <MiseWordmark inverse />
            <p className={styles.hero}>
              Mise <em>en place</em>
            </p>
          </div>
        </div>
        <p className={styles.slogan}>{t('sample.slogan')}</p>
      </Panel>

      <Panel title={t('sections.buttons')}>
        <div className={styles.row}>
          <Button icon={<Plus />}>{t('sample.primary')}</Button>
          <Button variant="secondary">{t('sample.secondary')}</Button>
          <Button variant="text">{t('sample.text')}</Button>
          <Button disabled>{t('sample.primary')}</Button>
        </div>
        <div className={styles.fields}>
          <Field label={t('sample.login')} hint={t('sample.loginHint')} autoComplete="off" />
          <Field label={t('sample.password')} type="password" error={t('sample.passwordError')} />
        </div>
      </Panel>

      <SummaryStrip
        label={t('sections.summary')}
        items={[
          { id: 'stop', value: 2, tone: 'crit', phrase: t('sample.stopPhrase'), linkLabel: t('sample.stopLink'), to: '/ui' },
          { id: 'exp', value: 7, tone: 'warn', phrase: t('sample.expiringPhrase'), linkLabel: t('sample.expiringLink'), to: '/ui' },
          { id: 'ord', value: 86, tone: 'neutral', phrase: t('sample.ordersPhrase'), linkLabel: t('sample.ordersLink'), to: '/ui' },
        ]}
      />

      <div className={styles.columns}>
        <Panel title={t('sections.offers')} subtitle={t('sections.offersHint')} titleId="ui-offers">
          {offers.map((offer, index) => (
            <HotOfferRow
              key={offer.dish}
              rank={index + 1}
              dishName={offer.dish}
              verdict={offerVerdict(offer.score)}
              verdictText={f.verdict(offer.score)}
              reason={offer.reason}
              calculation={<p>{t('sample.calculation')}</p>}
            />
          ))}
        </Panel>
        <Panel title={t('sections.stop')} titleId="ui-stop">
          <StopListRow
            group="out"
            groupLabel={t('sample.out')}
            dishName={t('sample.tiramisu')}
            reason={t('sample.tiramisuReason', { left: f.qty(80, 'Gram'), need: f.qty(120, 'Gram') })}
          />
          <StopListRow
            group="soon"
            groupLabel={t('sample.soon')}
            dishName={t('sample.bruschetta')}
            reason={t('sample.bruschettaReason', { runway: f.runway(3.2).toLocaleLowerCase(f.locale) })}
          />
        </Panel>
      </div>

      <Panel title={t('sections.expiry')} subtitle={t('sections.expiryHint')} action={<Button variant="text">{t('sample.text')}</Button>}>
        <ExpiryList
          label={t('sections.expiry')}
          items={[
            { id: '1', productName: t('sample.salmon'), quantity: f.qty(2400, 'Gram'), when: f.when(inHours(6), 'Critical') },
            { id: '2', productName: t('sample.cream'), quantity: f.qty(1800, 'Milliliter'), when: f.when(inHours(20), 'Warning') },
            { id: '3', productName: t('sample.tomatoes'), quantity: f.qty(6000, 'Gram'), when: f.when(inHours(30), 'Warning') },
            { id: '4', productName: t('sample.arugula'), quantity: f.qty(500, 'Gram'), when: f.when(inHours(80), 'Fresh') },
          ]}
        />
      </Panel>

      <div className={styles.columns}>
        <Panel title={t('sections.statuses')}>
          <div className={styles.stack}>
            <StatusMark tone="fresh">{f.when(inHours(100), 'Fresh').text}</StatusMark>
            <StatusMark tone="warn">{f.when(inHours(20), 'Warning').text}</StatusMark>
            <StatusMark tone="crit">{f.when(inHours(3), 'Critical').text}</StatusMark>
            <StatusMark tone="expired">{f.when(inHours(-1), 'Expired').text}</StatusMark>
            <ShelfLifeBar remainingFraction={0.18} when={f.when(inHours(5), 'Critical')} />
            <ShelfLifeBar remainingFraction={0.72} when={f.when(inHours(120), 'Fresh')} />
          </div>
        </Panel>
        <Panel title={t('sections.live')}>
          <div className={styles.stack}>
            <LiveIndicator status="live" />
            <LiveIndicator status="reconnecting" />
            <LiveIndicator status="offline" />
            <LiveIndicator status="live" short />
          </div>
        </Panel>
      </div>

      <Panel title={t('sections.states')}>
        <EmptyState>{t('sample.emptyHint')}</EmptyState>
        <LoadingState />
        <ErrorState error={new ApiError(0, 'offline')} onRetry={() => undefined} />
      </Panel>
    </main>
  )
}

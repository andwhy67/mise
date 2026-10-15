import { Copy, ExternalLink, Printer, RefreshCw } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useGuestMenuSettings, useRegenerateMenuKey, useUpdateGuestMenuSettings, type GuestMenuSettings } from '@/api/guest'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { usePrint } from '@/components/PrintArea'
import { QrCode } from '@/components/QrCode'
import { ErrorState, LoadingState } from '@/components/StateBlocks'
import { Switch } from '@/components/Switch'
import { qrDataUrl } from '@/lib/qr'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { menuUrl, qrCards, type QrCard } from './qrCards'
import styles from './QrMenuPage.module.scss'

/** Менеджер: включить заказы гостей, число столов, ссылка на меню, перевыпуск ключа и печать QR-карточек. */
export function QrMenuPage() {
  const { t } = useTranslation('guest')
  const settings = useGuestMenuSettings()

  return (
    <Page title={t('admin.title')} subtitle={t('admin.subtitle')}>
      {settings.isPending ? (
        <LoadingState lines={6} />
      ) : settings.error ? (
        <ErrorState error={settings.error} onRetry={() => void settings.refetch()} />
      ) : (
        <QrMenuContent settings={settings.data} />
      )}
    </Page>
  )
}

function QrMenuContent({ settings }: { settings: GuestMenuSettings }) {
  const { t } = useTranslation('guest')
  const errorText = useErrorText()
  const update = useUpdateGuestMenuSettings()
  const regenerate = useRegenerateMenuKey()
  const { print, portal } = usePrint()
  const [tables, setTables] = useState(String(settings.tables))
  const [confirming, setConfirming] = useState(false)
  const link = menuUrl(window.location.origin, settings.menuKey)
  const cards = qrCards(link, settings.tables)
  const tablesValue = Number(tables)
  const tablesValid = /^\d{1,3}$/.test(tables) && tablesValue >= 0 && tablesValue <= 200

  const save = (next: { ordering: boolean; tables: number }) =>
    update.mutate(next, {
      onSuccess: () => toast.show(t('admin.saved')),
      onError: (cause) => toast.show(errorText(cause), 'crit'),
    })

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(link)
      toast.show(t('admin.copied'))
    } catch {
      window.prompt(t('admin.link'), link)
    }
  }

  const printCards = async () => {
    // Картинки — заранее: печать стартует сразу после отрисовки, ждать асинхронный QR она не будет.
    const images = await Promise.all(cards.map((card) => qrDataUrl(card.url, 480)))
    print(<PrintedCards cards={cards} images={images} ordering={settings.ordering} />)
  }

  return (
    <div className={styles.layout}>
      <Panel title={t('admin.settings')} titleId="qr-settings">
        <div className={styles.setting}>
          <div>
            <p className={styles.settingName}>{t('admin.ordering')}</p>
            <p className={styles.settingHint}>{t('admin.orderingHint')}</p>
          </div>
          <Switch
            id="guest-ordering"
            checked={settings.ordering}
            label={t('admin.ordering')}
            disabled={update.isPending}
            onChange={(ordering) => save({ ordering, tables: settings.tables })}
          />
        </div>
        <form
          className={styles.tablesForm}
          onSubmit={(e) => {
            e.preventDefault()
            if (tablesValid) save({ ordering: settings.ordering, tables: tablesValue })
          }}
        >
          <Field
            label={t('admin.tables')}
            hint={t('admin.tablesHint')}
            inputMode="numeric"
            value={tables}
            onChange={(e) => setTables(e.target.value)}
            error={tablesValid ? undefined : t('admin.tablesHint')}
          />
          <Button type="submit" variant="secondary" disabled={!tablesValid || update.isPending || tablesValue === settings.tables}>
            {t('admin.save')}
          </Button>
        </form>
      </Panel>

      <Panel title={t('admin.link')} subtitle={t('admin.linkHint')} titleId="qr-link">
        <div className={styles.linkRow}>
          <QrCode value={link} label={t('admin.qrAlt', { label: t('admin.general') })} size={160} className={styles.qr} />
          <div className={styles.linkBody}>
            <code className={styles.url}>{link}</code>
            <div className={styles.actions}>
              <a className={styles.textLink} href={link} target="_blank" rel="noreferrer">
                <ExternalLink aria-hidden="true" strokeWidth={1.8} />
                {t('admin.open')}
              </a>
              <Button variant="text" icon={<Copy strokeWidth={1.8} />} onClick={() => void copy()}>
                {t('admin.copy')}
              </Button>
            </div>
          </div>
        </div>
        <div className={styles.regenerate}>
          {confirming ? (
            <>
              <p className={styles.settingHint} role="alert">
                {t('admin.regenerateHint')}
              </p>
              <div className={styles.actions}>
                <Button
                  variant="secondary"
                  disabled={regenerate.isPending}
                  onClick={() =>
                    regenerate.mutate(undefined, {
                      onSuccess: () => {
                        setConfirming(false)
                        toast.show(t('admin.regenerated'), 'warn')
                      },
                      onError: (cause) => toast.show(errorText(cause), 'crit'),
                    })
                  }
                >
                  {t('admin.regenerateConfirm')}
                </Button>
                <Button variant="text" onClick={() => setConfirming(false)}>
                  {t('admin.cancel')}
                </Button>
              </div>
            </>
          ) : (
            <Button variant="text" icon={<RefreshCw strokeWidth={1.8} />} onClick={() => setConfirming(true)}>
              {t('admin.regenerate')}
            </Button>
          )}
        </div>
      </Panel>

      <Panel
        title={t('admin.print')}
        subtitle={t('admin.printHint')}
        titleId="qr-print"
        action={
          <Button icon={<Printer strokeWidth={1.8} />} onClick={() => void printCards()}>
            {t('admin.print')}
          </Button>
        }
        className={styles.wide}
      >
        <ul className={styles.cards} aria-label={t('admin.preview')}>
          {cards.slice(0, 7).map((card) => (
            <li key={card.url} className={styles.card}>
              <QrCode value={card.url} label={t('admin.qrAlt', { label: cardTitle(card, t) })} size={120} />
              <span className={styles.cardTitle}>{cardTitle(card, t)}</span>
            </li>
          ))}
          {cards.length > 7 && <li className={styles.more}>+{cards.length - 7}</li>}
        </ul>
      </Panel>
      {portal}
    </div>
  )
}

const cardTitle = (card: QrCard, t: (key: string, options?: Record<string, unknown>) => string) =>
  card.table === null ? t('admin.general') : t('admin.tableCard', { table: card.table })

function PrintedCards({ cards, images, ordering }: { cards: QrCard[]; images: string[]; ordering: boolean }) {
  const { t } = useTranslation('guest')
  return (
    <div className={styles.printSheet}>
      {cards.map((card, index) => (
        <article key={card.url} className={styles.printCard}>
          <h2 className={styles.printTitle}>{cardTitle(card, t)}</h2>
          <img src={images[index]} alt={t('admin.qrAlt', { label: cardTitle(card, t) })} className={styles.printQr} />
          <p className={styles.printText}>{ordering ? t('admin.scanOrder') : t('admin.scanView')}</p>
        </article>
      ))}
    </div>
  )
}

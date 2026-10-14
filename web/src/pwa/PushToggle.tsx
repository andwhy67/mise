import { BellRing } from 'lucide-react'
import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/Button'
import { Switch } from '@/components/Switch'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { usePush } from './push'
import styles from './AppSettings.module.scss'

/** «Уведомления на телефон»: включить/выключить, проверить; честные подсказки, если устройство не может. */
export function PushToggle() {
  const { t, i18n } = useTranslation()
  const errorText = useErrorText()
  const push = usePush(i18n.resolvedLanguage ?? i18n.language)
  const id = useId()
  if (push.state === 'loading') return null

  const on = push.state === 'on'
  const available = on || push.state === 'off'
  const hint = available ? t('pwa.pushText') : t(`pwa.push.${push.state}`)

  const toggle = (next: boolean) =>
    void (next ? push.enable() : push.disable()).catch((cause: unknown) => toast.show(errorText(cause), 'crit'))

  return (
    <div className={styles.row}>
      <span className={styles.icon} aria-hidden="true">
        <BellRing />
      </span>
      <div className={styles.body}>
        <p className={styles.title}>{t('pwa.pushTitle')}</p>
        <p className={styles.text}>{hint}</p>
        {on && (
          <Button
            variant="text"
            className={styles.inline}
            onClick={() =>
              void push
                .test()
                .then(({ sent }) => (sent > 0 ? toast.show(t('pwa.pushTestSent')) : toast.show(t('pwa.pushTestFailed'), 'warn')))
                .catch((cause: unknown) => toast.show(errorText(cause), 'crit'))
            }
          >
            {t('pwa.pushTest')}
          </Button>
        )}
      </div>
      {available && (
        <div className={styles.control}>
          <Switch id={id} checked={on} disabled={push.busy} label={t('pwa.pushTitle')} onChange={toggle} />
        </div>
      )}
    </div>
  )
}

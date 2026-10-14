import { Download, MonitorSmartphone, Share } from 'lucide-react'
import { useId, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/Button'
import { Panel } from '@/components/Panel'
import { Switch } from '@/components/Switch'
import { toast } from '@/lib/toast'
import { isIos, isStandalone, promptInstall, useCanInstall } from './install'
import { PushToggle } from './PushToggle'
import { setKeepAwake, useKeepAwake, wakeLockSupported } from './wakeLock'
import styles from './AppSettings.module.scss'

/** «Приложение»: установка на экран, уведомления на телефон, «не гасить экран». Только то, что поддерживает устройство. */
export function AppSettings() {
  const { t } = useTranslation()
  return (
    <Panel title={t('pwa.title')} subtitle={t('pwa.subtitle')} titleId="app-settings">
      <div className={styles.list}>
        <InstallRow />
        <PushToggle />
        {wakeLockSupported() && <KeepAwakeRow />}
      </div>
    </Panel>
  )
}

function Row({ icon, title, text, children }: { icon: ReactNode; title: string; text: ReactNode; children?: ReactNode }) {
  return (
    <div className={styles.row}>
      <span className={styles.icon} aria-hidden="true">
        {icon}
      </span>
      <div className={styles.body}>
        <p className={styles.title}>{title}</p>
        <p className={styles.text}>{text}</p>
      </div>
      {children && <div className={styles.control}>{children}</div>}
    </div>
  )
}

function InstallRow() {
  const { t } = useTranslation()
  const canInstall = useCanInstall()
  if (isStandalone()) return <Row icon={<MonitorSmartphone />} title={t('pwa.installed')} text={t('pwa.installedText')} />
  if (canInstall)
    return (
      <Row icon={<Download />} title={t('pwa.install')} text={t('pwa.installText')}>
        <Button variant="secondary" onClick={() => void promptInstall().then((ok) => ok && toast.show(t('pwa.installDone')))}>
          {t('pwa.installAction')}
        </Button>
      </Row>
    )
  // iPhone и iPad: установить можно только через «Поделиться» в Safari.
  if (isIos()) return <Row icon={<Share />} title={t('pwa.install')} text={t('pwa.installIos')} />
  return null
}

function KeepAwakeRow() {
  const { t } = useTranslation()
  const keepAwake = useKeepAwake()
  const id = useId()
  return (
    <Row icon={<MonitorSmartphone />} title={t('pwa.keepAwake')} text={t('pwa.keepAwakeText')}>
      <Switch id={id} checked={keepAwake} label={t('pwa.keepAwake')} onChange={(next) => void setKeepAwake(next)} />
    </Row>
  )
}

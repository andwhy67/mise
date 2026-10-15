import { Copy, Printer } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useCreateSupplyRequest } from '@/api/mutations'
import { useProducts } from '@/api/queries'
import type { SupplyRequest, SupplySuggestion } from '@/api/types'
import { Button } from '@/components/Button'
import { usePrint } from '@/components/PrintArea'
import { Sheet } from '@/components/Sheet'
import { ErrorState, LoadingState } from '@/components/StateBlocks'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat } from '@/lib/useFormat'
import { useOnline } from '@/pwa/network'
import {
  baseQuantity,
  groupBySupplier,
  groupEstimate,
  includedLines,
  requestText,
  toRequest,
  validateGroup,
  type RequestTextLabels,
  type SupplierGroup,
} from './supplierRequest'
import styles from './SupplierRequestSheet.module.scss'

const UNIT_KEY = { Gram: 'kg', Milliliter: 'l', Piece: 'pcs' } as const

/**
 * Заявки поставщикам из «Что заказать» (без ожидаемой даты и статусов — решение заказчика): по поставщикам,
 * количества можно поправить или снять строку. «Скопировать» — текст для мессенджера/почты, «Печать» — бумага или PDF.
 * Номер заявки выдаёт сервер и пишет её в журнал действий; повторное копирование той же заявки номер не меняет.
 */
export function SupplierRequestSheet({ open, items, onClose }: { open: boolean; items: SupplySuggestion[]; onClose: () => void }) {
  const { t } = useTranslation('supplies')
  const products = useProducts()
  return (
    <Sheet open={open} onClose={onClose} title={t('request.title')} subtitle={t('request.subtitle')} wide>
      {products.isPending ? (
        <LoadingState lines={4} />
      ) : products.isError ? (
        <ErrorState error={products.error} onRetry={() => void products.refetch()} />
      ) : (
        open && <RequestGroups initial={groupBySupplier(items, products.data)} />
      )}
    </Sheet>
  )
}

function RequestGroups({ initial }: { initial: SupplierGroup[] }) {
  const [groups, setGroups] = useState(initial)
  // Зарегистрированные заявки по содержимому: копия и печать одной и той же заявки — один номер и одна запись в журнале.
  const [registered, setRegistered] = useState<Map<string, SupplyRequest>>(new Map())
  const { print, portal } = usePrint()

  const update = (index: number, next: SupplierGroup) => setGroups((current) => current.map((g, i) => (i === index ? next : g)))

  return (
    <div className={styles.groups}>
      {groups.map((group, index) => (
        <GroupCard
          key={group.supplier ?? '—'}
          group={group}
          known={registered.get(JSON.stringify(toRequest(group)))}
          onChange={(next) => update(index, next)}
          onRegistered={(request) => setRegistered((current) => new Map(current).set(JSON.stringify(toRequest(group)), request))}
          onPrint={(request) => print(<PrintedRequest group={group} request={request} />)}
        />
      ))}
      {portal}
    </div>
  )
}

function useTextLabels(): RequestTextLabels {
  const { t } = useTranslation('supplies')
  return {
    title: (number) => t('request.text.title', { number }),
    supplier: (name) => t('request.text.supplier', { name }),
    noSupplier: t('request.text.noSupplier'),
    date: (date) => t('request.text.date', { date }),
    line: (index, product, qty) => t('request.text.line', { index, product, qty }),
    estimate: (sum) => t('request.text.estimate', { sum }),
    estimatePartial: (sum, count) => t('request.text.estimatePartial', { sum, count }),
  }
}

/** Копирование: Clipboard API (https, localhost), иначе — старый способ через выделение текста. */
async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    const area = document.createElement('textarea')
    area.value = text
    area.setAttribute('readonly', '')
    area.style.position = 'fixed'
    area.style.opacity = '0'
    document.body.append(area)
    area.select()
    const ok = document.execCommand('copy')
    area.remove()
    return ok
  }
}

interface GroupCardProps {
  group: SupplierGroup
  known: SupplyRequest | undefined
  onChange: (group: SupplierGroup) => void
  onRegistered: (request: SupplyRequest) => void
  onPrint: (request: SupplyRequest) => void
}

function GroupCard({ group, known, onChange, onRegistered, onPrint }: GroupCardProps) {
  const { t } = useTranslation('supplies')
  const { t: tf } = useTranslation('format')
  const f = useFormat()
  const labels = useTextLabels()
  const errorText = useErrorText()
  const mutation = useCreateSupplyRequest()
  const online = useOnline()
  const [errors, setErrors] = useState<Record<string, string>>({})
  const estimate = groupEstimate(group)
  const included = includedLines(group)
  const title = group.supplier ?? t('request.noSupplier')

  const setLine = (productId: string, patch: Partial<SupplierGroup['lines'][number]>) =>
    onChange({ ...group, lines: group.lines.map((l) => (l.productId === productId ? { ...l, ...patch } : l)) })

  /** Номер заявки — с сервера (и запись в журнал); уже выданный для того же содержимого — переиспользуем. */
  const register = (then: (request: SupplyRequest) => void) => {
    const found = validateGroup(group)
    setErrors(found)
    if (Object.keys(found).length) return
    if (known) return then(known)
    mutation.mutate(toRequest(group), {
      onSuccess: (request) => {
        onRegistered(request)
        then(request)
      },
      onError: (cause) => toast.show(errorText(cause), 'crit'),
    })
  }

  const copy = () =>
    register(async (request) => {
      const text = requestText(group, { number: request.number, date: f.date(request.createdAt) }, labels, f)
      const ok = await copyText(text)
      toast.show(t(ok ? 'request.copied' : 'request.copyFailed', { number: request.number }), ok ? undefined : 'crit')
    })

  return (
    <section className={styles.group} aria-label={title}>
      <header className={styles.groupHead}>
        <h3 className={styles.supplier}>{title}</h3>
        {known && <span className={styles.number}>{t('request.number', { number: known.number })}</span>}
      </header>
      <ul className={styles.lines}>
        {group.lines.map((line) => (
          <li key={line.productId} className={`${styles.line} ${line.include ? '' : styles.excluded}`}>
            <label className={styles.include}>
              <input type="checkbox" checked={line.include} onChange={(e) => setLine(line.productId, { include: e.target.checked })} />
              <span>{line.productName}</span>
            </label>
            <span className={styles.qty}>
              <input
                className={styles.qtyInput}
                inputMode="decimal"
                aria-label={t('request.qtyLabel', { product: line.productName })}
                aria-invalid={errors[line.productId] ? true : undefined}
                value={line.quantity}
                disabled={!line.include}
                onChange={(e) => setLine(line.productId, { quantity: e.target.value })}
              />
              <span className={styles.unit}>{tf(`unit.${UNIT_KEY[line.unit]}`)}</span>
            </span>
            <span className={styles.cost}>
              {line.price != null && baseQuantity(line) !== null ? f.money((baseQuantity(line)! / (line.unit === 'Piece' ? 1 : 1000)) * line.price) : '—'}
            </span>
          </li>
        ))}
      </ul>
      {Object.keys(errors).length > 0 && <p className={styles.error}>{t('request.errors.quantity')}</p>}
      <footer className={styles.groupFoot}>
        <p className={styles.estimate}>
          {estimate.total > 0 ? t('request.estimate', { sum: f.money(estimate.total) }) : t('request.noEstimate')}
          {estimate.unpriced > 0 && estimate.total > 0 && <span className={styles.note}> {t('request.unpriced', { count: estimate.unpriced })}</span>}
        </p>
        <div className={styles.actions}>
          <Button variant="secondary" icon={<Copy />} disabled={included.length === 0 || mutation.isPending || !online} onClick={copy}>
            {t('request.copy')}
          </Button>
          <Button variant="secondary" icon={<Printer />} disabled={included.length === 0 || mutation.isPending || !online} onClick={() => register(onPrint)}>
            {t('request.print')}
          </Button>
        </div>
      </footer>
    </section>
  )
}

/** Печатный бланк заявки: номер, кому, дата, таблица строк, ориентировочная сумма. */
function PrintedRequest({ group, request }: { group: SupplierGroup; request: SupplyRequest }) {
  const { t } = useTranslation('supplies')
  const f = useFormat()
  return (
    <article>
      <h1>{t('request.sheet.heading')}</h1>
      <p>
        {t('request.sheet.number', { number: request.number })}
        <br />
        {group.supplier ? t('request.text.supplier', { name: group.supplier }) : t('request.text.noSupplier')}
        <br />
        {t('request.text.date', { date: f.date(request.createdAt) })}
      </p>
      <table>
        <thead>
          <tr>
            <th>№</th>
            <th>{t('request.sheet.product')}</th>
            <th className="num">{t('request.sheet.qty')}</th>
            <th className="num">{t('request.sheet.price')}</th>
            <th className="num">{t('request.sheet.sum')}</th>
          </tr>
        </thead>
        <tbody>
          {request.lines.map((line, index) => (
            <tr key={line.productId}>
              <td>{index + 1}</td>
              <td>{line.productName}</td>
              <td className="num">{f.qtyExact(line.quantity, line.unit)}</td>
              <td className="num">{line.price != null ? f.money(line.price) : '—'}</td>
              <td className="num">{line.cost != null ? f.money(line.cost) : '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {request.estimatedTotal > 0 && (
        <p>
          <strong>{t('request.estimate', { sum: f.money(request.estimatedTotal) })}</strong>
        </p>
      )}
      <p>{t('request.sheet.footer')}</p>
    </article>
  )
}

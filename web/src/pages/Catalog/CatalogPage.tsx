import { Plus, Search } from 'lucide-react'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { useDishes, useProducts } from '@/api/queries'
import type { Dish, Product } from '@/api/types'
import { Button } from '@/components/Button'
import { Page } from '@/components/Page'
import { Panel } from '@/components/Panel'
import { Segmented } from '@/components/Segmented'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateBlocks'
import { StatusMark } from '@/components/StatusMark'
import { useFormat } from '@/lib/useFormat'
import { DishSheet } from './DishSheet'
import { ProductSheet } from './ProductSheet'
import styles from './CatalogPage.module.scss'

type Tab = 'products' | 'dishes'
/** Что открыто в листе: новая запись или существующая. */
type Editing<T> = { item: T | null } | null

/** Справочники (менеджер): продукты с нормами хранения и блюда с составом на порцию. */
export function CatalogPage() {
  const { t } = useTranslation('catalog')
  const [params, setParams] = useSearchParams()
  const tab: Tab = params.get('tab') === 'dishes' ? 'dishes' : 'products'
  const [product, setProduct] = useState<Editing<Product>>(null)
  const [dish, setDish] = useState<Editing<Dish>>(null)

  return (
    <Page
      title={t('title')}
      subtitle={t('subtitle')}
      actions={
        <Button icon={<Plus />} onClick={() => (tab === 'products' ? setProduct({ item: null }) : setDish({ item: null }))}>
          {t(tab === 'products' ? 'products.add' : 'dishes.add')}
        </Button>
      }
    >
      <Segmented
        label={t('tabs.label')}
        value={tab}
        options={[
          { value: 'products', label: t('tabs.products') },
          { value: 'dishes', label: t('tabs.dishes') },
        ]}
        onChange={(value) => setParams(value === 'products' ? {} : { tab: value }, { replace: true })}
      />
      {tab === 'products' ? <ProductsList onOpen={(item) => setProduct({ item })} /> : <DishesList onOpen={(item) => setDish({ item })} />}
      <ProductSheet key={product?.item?.id ?? (product ? 'new' : 'closed')} open={product !== null} product={product?.item ?? null} onClose={() => setProduct(null)} />
      <DishSheet key={dish?.item?.id ?? (dish ? 'new' : 'closed-dish')} open={dish !== null} dish={dish?.item ?? null} onClose={() => setDish(null)} />
    </Page>
  )
}

function SearchBox({ value, onChange, label }: { value: string; onChange: (value: string) => void; label: string }) {
  const id = useId()
  return (
    <div className={styles.search}>
      <label htmlFor={id} className="visually-hidden">
        {label}
      </label>
      <Search aria-hidden="true" strokeWidth={1.8} />
      <input id={id} type="search" placeholder={label} value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

function ProductsList({ onOpen }: { onOpen: (product: Product) => void }) {
  const { t } = useTranslation('catalog')
  const f = useFormat()
  const query = useProducts()
  const [search, setSearch] = useState('')
  const needle = search.trim().toLocaleLowerCase()
  const items = (query.data ?? [])
    .filter((p) => !needle || p.name.toLocaleLowerCase().includes(needle) || p.category?.toLocaleLowerCase().includes(needle))
    .sort((a, b) => (a.category ?? '').localeCompare(b.category ?? '') || a.name.localeCompare(b.name))

  return (
    <Panel>
      <SearchBox value={search} onChange={setSearch} label={t('products.search')} />
      {query.isPending ? (
        <LoadingState lines={6} />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t(needle ? 'notFound' : 'products.empty')} />
      ) : (
        <ul className={styles.list}>
          {items.map((p) => (
            <li key={p.id}>
              <button type="button" className={styles.row} onClick={() => onOpen(p)}>
                <span className={styles.name}>
                  <span className={styles.title}>{p.name}</span>
                  <span className={styles.meta}>{[p.category, p.storageType].filter(Boolean).join(' · ') || t('noCategory')}</span>
                </span>
                <span className={styles.cell}>
                  {p.defaultShelfLifeHours ? t('products.shelfLife', { days: f.number(p.defaultShelfLifeHours / 24) }) : t('products.noShelfLife')}
                </span>
                <span className={styles.cell}>{t('products.minStock', { qty: f.qty(p.minStockQty, p.unit) })}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

function DishesList({ onOpen }: { onOpen: (dish: Dish) => void }) {
  const { t } = useTranslation('catalog')
  const f = useFormat()
  const query = useDishes()
  const [search, setSearch] = useState('')
  const needle = search.trim().toLocaleLowerCase()
  const items = (query.data ?? [])
    .filter((d) => !needle || d.name.toLocaleLowerCase().includes(needle) || d.category?.toLocaleLowerCase().includes(needle))
    .sort((a, b) => (a.category ?? '').localeCompare(b.category ?? '') || a.name.localeCompare(b.name))

  return (
    <Panel>
      <SearchBox value={search} onChange={setSearch} label={t('dishes.search')} />
      {query.isPending ? (
        <LoadingState lines={6} />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={t(needle ? 'notFound' : 'dishes.empty')} />
      ) : (
        <ul className={styles.list}>
          {items.map((d) => (
            <li key={d.id}>
              <button type="button" className={styles.row} onClick={() => onOpen(d)}>
                <span className={styles.name}>
                  <span className={styles.title}>{d.name}</span>
                  <span className={styles.meta}>{d.category || t('noCategory')}</span>
                </span>
                <span className={styles.cell}>{t('dishes.ingredients', { count: d.ingredients.length })}</span>
                <span className={styles.cell}>
                  <span className={styles.price}>{f.money(d.price)}</span>
                  {!d.isActive && (
                    <StatusMark tone="crit" weight="regular">
                      {t('dishes.offMenu')}
                    </StatusMark>
                  )}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

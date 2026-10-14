import { Check, ChevronDown, Search } from 'lucide-react'
import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import styles from './Combobox.module.scss'

export interface ComboboxOption {
  value: string
  label: string
  /** Заголовок группы (категория продукта). */
  group?: string
  /** Подсказка справа: единица, остаток. */
  hint?: string
}

interface ComboboxProps {
  label: string
  /** Скрыть метку визуально (повторяющиеся строки формы), оставив её для диктора. */
  hideLabel?: boolean
  value: string
  options: ComboboxOption[]
  onChange: (value: string) => void
  placeholder?: string
  error?: string
  className?: string
}

/** Сколько вариантов видно без прокрутки списка: окно не должно расти до края экрана. */
const LIST_MAX_PX = 264

/** Совпадение по началу слова выше, чем в середине: «лим» → сначала «Лимоны», потом «Филе лимонника». */
function rank(label: string, query: string) {
  const text = label.toLocaleLowerCase()
  const index = text.indexOf(query)
  if (index < 0) return -1
  return index === 0 ? 0 : /[\s(«"-]/.test(text[index - 1] ?? '') ? 1 : 2
}

/**
 * Выбор из длинного списка с поиском по буквам (ARIA combobox + listbox). Вместо нативного <select>:
 * тот на десктопе открывается во всю высоту экрана, до адресной строки.
 */
export function Combobox({ label, hideLabel, value, options, onChange, placeholder, error, className }: ComboboxProps) {
  const { t } = useTranslation()
  const id = useId()
  const listId = `${id}-list`
  const rootRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLUListElement>(null)
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const [upward, setUpward] = useState(false)
  const selected = options.find((o) => o.value === value)

  const filtered = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase()
    if (!needle) return options
    return options
      .map((option) => ({ option, score: rank(option.label, needle) }))
      .filter((x) => x.score >= 0)
      .sort((a, b) => a.score - b.score || a.option.label.localeCompare(b.option.label))
      .map((x) => x.option)
  }, [options, query])

  const show = (next: boolean) => {
    if (next && rootRef.current) {
      // Места снизу мало (строка у края листа или экрана) — открываем вверх, чтобы список не уезжал за край.
      const rect = rootRef.current.getBoundingClientRect()
      const container = rootRef.current.closest('dialog')?.getBoundingClientRect()
      const bottom = Math.min(window.innerHeight, container?.bottom ?? Infinity)
      const top = Math.max(0, container?.top ?? 0)
      setUpward(bottom - rect.bottom < LIST_MAX_PX + 16 && rect.top - top > bottom - rect.bottom)
      setActive(
        Math.max(
          0,
          options.findIndex((o) => o.value === value),
        ),
      )
    }
    if (!next) setQuery('')
    setOpen(next)
  }

  const choose = (option: ComboboxOption) => {
    onChange(option.value)
    show(false)
    inputRef.current?.focus()
  }

  // Активный вариант всегда виден при листании стрелками.
  useEffect(() => {
    if (!open) return
    listRef.current?.querySelector<HTMLElement>(`[data-index="${active}"]`)?.scrollIntoView({ block: 'nearest' })
  }, [active, open])

  // Клик мимо — закрыть.
  useEffect(() => {
    if (!open) return
    const onDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) show(false)
    }
    document.addEventListener('pointerdown', onDown)
    return () => document.removeEventListener('pointerdown', onDown)
    // show пересоздаётся на каждый рендер; подписка нужна только на время открытия.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      if (!open) return show(true)
      const step = event.key === 'ArrowDown' ? 1 : -1
      setActive((i) => Math.min(filtered.length - 1, Math.max(0, i + step)))
    } else if (event.key === 'Enter') {
      if (!open) return
      event.preventDefault()
      const option = filtered[active]
      if (option) choose(option)
    } else if (event.key === 'Escape') {
      if (!open) return
      // Esc закрывает список, а не лист (<dialog>), в котором стоит поле.
      event.preventDefault()
      event.stopPropagation()
      show(false)
    } else if (event.key === 'Tab') {
      show(false)
    }
  }

  // Группы — подряд, в порядке появления (после поиска порядок по релевантности, группы не показываем).
  const grouped = !query.trim() && filtered.some((o) => o.group)

  return (
    <div ref={rootRef} className={`${styles.field} ${className ?? ''}`}>
      <label htmlFor={id} className={hideLabel ? 'visually-hidden' : styles.label}>
        {label}
      </label>
      <div className={styles.control}>
        <Search className={styles.searchIcon} aria-hidden="true" strokeWidth={1.8} />
        <input
          ref={inputRef}
          id={id}
          className={styles.input}
          role="combobox"
          aria-expanded={open}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={open && filtered[active] ? `${id}-opt-${active}` : undefined}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? `${id}-error` : undefined}
          autoComplete="off"
          placeholder={selected ? selected.label : placeholder}
          value={open ? query : (selected?.label ?? '')}
          onChange={(e) => {
            setQuery(e.target.value)
            setActive(0)
            if (!open) show(true)
          }}
          onFocus={(e) => e.target.select()}
          onClick={() => !open && show(true)}
          onKeyDown={onKeyDown}
        />
        <ChevronDown className={`${styles.chevron} ${open ? styles.chevronOpen : ''}`} aria-hidden="true" strokeWidth={1.8} />
        {open && (
          <ul ref={listRef} id={listId} role="listbox" aria-label={label} className={`${styles.list} ${upward ? styles.upward : ''}`}>
            {filtered.length === 0 && <li className={styles.empty}>{t('combobox.nothing')}</li>}
            {filtered.map((option, index) => {
              const header = grouped && option.group && option.group !== filtered[index - 1]?.group
              return (
                <li key={option.value} role="presentation">
                  {header && (
                    <span className={styles.group} aria-hidden="true">
                      {option.group}
                    </span>
                  )}
                  <div
                    id={`${id}-opt-${index}`}
                    data-index={index}
                    role="option"
                    aria-selected={option.value === value}
                    className={`${styles.option} ${index === active ? styles.active : ''}`}
                    onPointerDown={(e) => e.preventDefault()}
                    onClick={() => choose(option)}
                    onPointerMove={() => setActive(index)}
                  >
                    <span className={styles.optionLabel}>{option.label}</span>
                    {option.hint && <span className={styles.hint}>{option.hint}</span>}
                    {option.value === value && <Check className={styles.check} aria-hidden="true" strokeWidth={2} />}
                  </div>
                </li>
              )
            })}
          </ul>
        )}
      </div>
      {error && (
        <p id={`${id}-error`} className={styles.error}>
          {error}
        </p>
      )}
    </div>
  )
}

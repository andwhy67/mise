import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { Link, type LinkProps } from 'react-router'
import styles from './Button.module.scss'

type Variant = 'primary' | 'secondary' | 'text'
type Size = 'md' | 'lg'

interface CommonProps {
  variant?: Variant
  size?: Size
  /** Иконка Lucide слева (декоративная). */
  icon?: ReactNode
  block?: boolean
  children: ReactNode
}

type ButtonProps = CommonProps & ButtonHTMLAttributes<HTMLButtonElement>
type LinkButtonProps = CommonProps & Omit<LinkProps, 'children'>

const classes = ({ variant = 'primary', size = 'md', block }: CommonProps, extra?: string) =>
  [styles.button, styles[variant], styles[size], block ? styles.block : '', extra ?? ''].join(' ')

/** Кнопка (DESIGN.md §5): primary — голубая, text — подчёркнутая ссылка-действие; высота ≥ 44px. */
export function Button({ variant, size, icon, block, children, className, type = 'button', ...rest }: ButtonProps) {
  return (
    <button type={type} className={classes({ variant, size, block, children }, className)} {...rest}>
      {icon && <span className={styles.icon} aria-hidden="true">{icon}</span>}
      {children}
    </button>
  )
}

/** То же, но переход по маршруту (семантически ссылка). */
export function LinkButton({ variant, size, icon, block, children, className, ...rest }: LinkButtonProps) {
  return (
    <Link className={classes({ variant, size, block, children }, className)} {...rest}>
      {icon && <span className={styles.icon} aria-hidden="true">{icon}</span>}
      {children}
    </Link>
  )
}

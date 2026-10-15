import {
  BarChart3,
  Ban,
  Bell,
  BookOpen,
  ClipboardList,
  Flame,
  Gauge,
  Home,
  Menu,
  Package,
  ScrollText,
  Truck,
  Users,
  type LucideIcon,
} from 'lucide-react'
import type { UserRole } from '@/auth/session'

export interface NavEntry {
  id: string
  path: string
  /** Ключ перевода в common:nav. */
  label: string
  /** Короткая подпись для нижней навигации. */
  shortLabel?: string
  icon: LucideIcon
  roles: UserRole[]
  /** Вторичный раздел: в сайдбаре — отдельной группой, на телефоне — в «Ещё». */
  secondary?: boolean
  /** Только в демо-ресторане (симулятор работает его служебной учёткой). */
  demoOnly?: boolean
}

const ALL: UserRole[] = ['Warehouse', 'Chef', 'Manager']

/** Разделы на языке кухни (DESIGN.md §1.1, п. 9); права совпадают с политиками API. */
export const NAV: NavEntry[] = [
  { id: 'home', path: '/', label: 'nav.home', icon: Home, roles: ALL },
  { id: 'stock', path: '/stock', label: 'nav.stock', icon: Package, roles: ALL },
  { id: 'supplies', path: '/supplies', label: 'nav.supplies', icon: Truck, roles: ['Warehouse', 'Manager'] },
  { id: 'orders', path: '/orders', label: 'nav.orders', shortLabel: 'nav.order', icon: ClipboardList, roles: ['Chef', 'Manager'] },
  { id: 'stoplist', path: '/stoplist', label: 'nav.stopList', shortLabel: 'nav.stop', icon: Ban, roles: ALL },
  { id: 'hot-offers', path: '/hot-offers', label: 'nav.hotOffers', icon: Flame, roles: ['Chef', 'Manager'] },
  { id: 'reports', path: '/reports', label: 'nav.reports', icon: BarChart3, roles: ['Manager'] },
  { id: 'alerts', path: '/alerts', label: 'nav.alerts', icon: Bell, roles: ALL, secondary: true },
  { id: 'catalog', path: '/catalog', label: 'nav.catalog', icon: BookOpen, roles: ['Manager'], secondary: true },
  { id: 'simulator', path: '/simulator', label: 'nav.simulator', icon: Gauge, roles: ['Manager'], secondary: true, demoOnly: true },
  { id: 'users', path: '/users', label: 'nav.users', icon: Users, roles: ['Manager'], secondary: true },
  { id: 'audit', path: '/audit', label: 'nav.audit', icon: ScrollText, roles: ['Manager'], secondary: true },
]

/** Нижняя навигация телефона: 4 главных раздела роли + «Ещё» (DESIGN.md §3, §6.4). */
const MOBILE_TABS: Record<Exclude<UserRole, 'Simulator'>, string[]> = {
  Manager: ['home', 'stock', 'orders', 'stoplist'],
  Chef: ['home', 'hot-offers', 'orders', 'stoplist'],
  Warehouse: ['home', 'stock', 'supplies', 'stoplist'],
}

export const MORE: NavEntry = { id: 'more', path: '/more', label: 'nav.more', icon: Menu, roles: ALL }

/** Разделы роли; demo = false — без разделов демо-ресторана (симулятора). */
export const navFor = (role: UserRole, demo = true) => NAV.filter((entry) => entry.roles.includes(role) && (demo || !entry.demoOnly))

export function mobileTabsFor(role: UserRole): NavEntry[] {
  const ids = MOBILE_TABS[role as keyof typeof MOBILE_TABS] ?? MOBILE_TABS.Chef
  return [...ids.map((id) => NAV.find((entry) => entry.id === id)!), MORE]
}

/** Разделы, которые на телефоне живут в «Ещё». */
export function moreFor(role: UserRole, demo = true): NavEntry[] {
  const tabs = new Set(mobileTabsFor(role).map((entry) => entry.id))
  return navFor(role, demo).filter((entry) => !tabs.has(entry.id))
}

export const canAccess = (role: UserRole, path: string, demo = true) =>
  path === '/' || navFor(role, demo).some((entry) => entry.path !== '/' && path.startsWith(entry.path)) || path === '/more'

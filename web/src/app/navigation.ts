import type { IconType } from 'react-icons'
import {
  LuBookOpen,
  LuChartColumn,
  LuClipboardList,
  LuFlame,
  LuLayoutDashboard,
  LuOctagonX,
  LuPackage,
  LuPlay,
  LuTimer,
  LuTruck,
  LuUsers,
  LuWarehouse,
} from 'react-icons/lu'

export interface NavItem {
  path: string
  label: string
  icon: IconType
  description: string
  /** Показывать в нижней навигации на телефоне (максимум 4 + «Ещё»). */
  primary?: boolean
}

export const navItems: NavItem[] = [
  { path: '/', label: 'Обзор', icon: LuLayoutDashboard, primary: true, description: 'Ключевые показатели смены, горячие предложения и алерты.' },
  { path: '/stock', label: 'Остатки', icon: LuWarehouse, primary: true, description: 'Остатки по продуктам и партиям, оставшийся срок, FEFO-порядок.' },
  { path: '/batches', label: 'Сроки', icon: LuTimer, description: 'Лента истекающих партий с фильтрами по статусу и горизонту.' },
  { path: '/supplies', label: 'Поставки', icon: LuTruck, description: 'Приём поставок, список и детали партий.' },
  { path: '/orders', label: 'Заказы', icon: LuClipboardList, primary: true, description: 'Создание заказов, проверка наличия и списание по FEFO.' },
  { path: '/stoplist', label: 'Стоп-лист', icon: LuOctagonX, primary: true, description: 'Блюда в стопе, ограничивающий продукт, прогноз и история.' },
  { path: '/hot-offers', label: 'Горячие', icon: LuFlame, description: 'Блюда, которые стоит продавать в первую очередь, и почему.' },
  { path: '/supply-suggestions', label: 'Что заказать', icon: LuPackage, description: 'Рекомендации поставок: что, сколько и насколько срочно.' },
  { path: '/analytics', label: 'Аналитика', icon: LuChartColumn, description: 'Списания, дефицитные ингредиенты, расход и KPI.' },
  { path: '/catalog', label: 'Справочники', icon: LuBookOpen, description: 'Продукты, блюда и их составы.' },
  { path: '/simulator', label: 'Симулятор', icon: LuPlay, description: 'Генератор поставок и заказов: сценарии и скорость.' },
  { path: '/users', label: 'Пользователи', icon: LuUsers, description: 'Пользователи, роли и журнал действий.' },
]

export const primaryNavItems = navItems.filter((item) => item.primary)

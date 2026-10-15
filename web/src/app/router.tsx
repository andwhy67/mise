import { createBrowserRouter, type RouteObject } from 'react-router'
import { AppLayout } from '@/layout/AppLayout'
import { DashboardPage } from '@/pages/Dashboard/DashboardPage'
import { LoginPage } from '@/pages/Login/LoginPage'
import { MorePage, NotFoundPage } from '@/pages/Misc/MiscPages'

/**
 * Экран грузится отдельным чанком при первом заходе: Главная и вход — сразу, остальное (в т.ч. Recharts
 * в «Отчётах») не тянется в первый экран телефона.
 */
const page = (load: () => Promise<{ default: RouteObject['Component'] }>): Pick<RouteObject, 'lazy'> => ({
  lazy: async () => ({ Component: (await load()).default }),
})

/** Маршруты совпадают с NAV (app/navigation.ts); доступ по роли проверяет AppLayout. */
export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <LoginPage mode="register" /> },
  // Ссылка-приглашение от менеджера: /join/K7Q2-PXM4 — код уже в поле.
  { path: '/join/:code?', element: <LoginPage mode="join" /> },
  // Демо компонентов (DESIGN.md §12): без входа, для проверки тем, языков и вёрстки.
  { path: '/ui', ...page(() => import('@/pages/Ui/UiPage').then((m) => ({ default: m.UiPage }))) },
  {
    path: '/',
    element: <AppLayout />,
    children: [
      { index: true, element: <DashboardPage /> },
      { path: 'stock', ...page(() => import('@/pages/Stock/StockPage').then((m) => ({ default: m.StockPage }))) },
      { path: 'supplies', ...page(() => import('@/pages/Supplies/SuppliesPage').then((m) => ({ default: m.SuppliesPage }))) },
      { path: 'orders', ...page(() => import('@/pages/Orders/OrdersPage').then((m) => ({ default: m.OrdersPage }))) },
      { path: 'stoplist', ...page(() => import('@/pages/StopList/StopListPage').then((m) => ({ default: m.StopListPage }))) },
      { path: 'hot-offers', ...page(() => import('@/pages/HotOffers/HotOffersPage').then((m) => ({ default: m.HotOffersPage }))) },
      { path: 'reports', ...page(() => import('@/pages/Reports/ReportsPage').then((m) => ({ default: m.ReportsPage }))) },
      { path: 'alerts', ...page(() => import('@/pages/Alerts/AlertsPage').then((m) => ({ default: m.AlertsPage }))) },
      { path: 'catalog', ...page(() => import('@/pages/Catalog/CatalogPage').then((m) => ({ default: m.CatalogPage }))) },
      { path: 'simulator', ...page(() => import('@/pages/Simulator/SimulatorPage').then((m) => ({ default: m.SimulatorPage }))) },
      { path: 'users', ...page(() => import('@/pages/Users/UsersPage').then((m) => ({ default: m.UsersPage }))) },
      { path: 'audit', ...page(() => import('@/pages/Audit/AuditPage').then((m) => ({ default: m.AuditPage }))) },
      { path: 'more', element: <MorePage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])

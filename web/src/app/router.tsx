import { createBrowserRouter } from 'react-router'
import { AppLayout } from '@/layout/AppLayout'
import { LoginPage } from '@/pages/Login/LoginPage'
import { MorePage } from '@/pages/More/MorePage'
import { NotFoundPage } from '@/pages/NotFound/NotFoundPage'
import { OverviewPage } from '@/pages/Overview/OverviewPage'
import { PlaceholderPage } from '@/pages/Placeholder/PlaceholderPage'
import { navItems } from './navigation'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    path: '/',
    element: <AppLayout />,
    children: [
      { index: true, element: <OverviewPage /> },
      ...navItems
        .filter((item) => item.path !== '/')
        .map((item) => ({ path: item.path.slice(1), element: <PlaceholderPage item={item} /> })),
      { path: 'more', element: <MorePage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])

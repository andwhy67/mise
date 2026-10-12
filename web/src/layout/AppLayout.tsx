import { Badge, HStack, IconButton, Text } from '@chakra-ui/react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { LuEllipsis, LuLogOut } from 'react-icons/lu'
import { navItems, primaryNavItems } from '@/app/navigation'
import { MiseWordmark } from '@/components/brand/MiseWordmark'
import { ColorModeButton } from '@/components/ui/color-mode'
import styles from './AppLayout.module.scss'

export function AppLayout() {
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const current = navItems.find((item) => item.path === pathname)
  const isMoreActive = !primaryNavItems.some((item) => item.path === pathname)

  return (
    <div className={styles.shell}>
      <nav className={styles.sidebar} aria-label="Основная навигация">
        <Link to="/" className={styles.brand} aria-label="Mise — на главную">
          <MiseWordmark />
        </Link>
        {navItems.map(({ path, label, icon: Icon }) => (
          <NavLink key={path} to={path} end={path === '/'} className={styles.navLink}>
            <Icon aria-hidden />
            {label}
          </NavLink>
        ))}
      </nav>

      <div className={styles.main}>
        <header className={styles.header}>
          <Text as="span" fontWeight="600" fontSize="md" truncate>
            {current?.label ?? 'Mise'}
          </Text>
          <HStack gap={1}>
            {/* Live-индикатор заработает с SignalR в фазе 4. */}
            <Badge variant="subtle" colorPalette="gray" title="Обновления в реальном времени появятся позже">
              ● Live скоро
            </Badge>
            <ColorModeButton />
            <IconButton variant="ghost" size="sm" aria-label="Выйти из смены" title="Выйти из смены" onClick={() => navigate('/login')}>
              <LuLogOut />
            </IconButton>
          </HStack>
        </header>

        <main className={styles.content}>
          <Outlet />
        </main>
      </div>

      <nav className={styles.bottomNav} aria-label="Навигация">
        {primaryNavItems.map(({ path, label, icon: Icon }) => (
          <NavLink key={path} to={path} end={path === '/'} className={styles.bottomLink}>
            <Icon aria-hidden />
            <span>{label}</span>
          </NavLink>
        ))}
        <NavLink to="/more" className={styles.bottomLink} aria-current={isMoreActive ? 'page' : undefined}>
          <LuEllipsis aria-hidden />
          <span>Ещё</span>
        </NavLink>
      </nav>
    </div>
  )
}

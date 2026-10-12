import { IconButton, type IconButtonProps } from '@chakra-ui/react'
import { ThemeProvider, useTheme, type ThemeProviderProps } from 'next-themes'
import { LuMoon, LuSun } from 'react-icons/lu'

export type ColorMode = 'light' | 'dark'

export function ColorModeProvider(props: ThemeProviderProps) {
  return <ThemeProvider attribute="class" disableTransitionOnChange defaultTheme="system" {...props} />
}

export function useColorMode() {
  const { resolvedTheme, setTheme } = useTheme()
  const colorMode: ColorMode = resolvedTheme === 'dark' ? 'dark' : 'light'
  return {
    colorMode,
    setColorMode: (mode: ColorMode) => setTheme(mode),
    toggleColorMode: () => setTheme(colorMode === 'dark' ? 'light' : 'dark'),
  }
}

export function ColorModeButton(props: Omit<IconButtonProps, 'aria-label'>) {
  const { colorMode, toggleColorMode } = useColorMode()
  const label = colorMode === 'dark' ? 'Включить светлую тему' : 'Включить тёмную тему'
  return (
    <IconButton variant="ghost" size="sm" aria-label={label} title={label} onClick={toggleColorMode} {...props}>
      {colorMode === 'dark' ? <LuSun /> : <LuMoon />}
    </IconButton>
  )
}

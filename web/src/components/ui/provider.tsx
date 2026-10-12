import { ChakraProvider } from '@chakra-ui/react'
import { QueryClientProvider } from '@tanstack/react-query'
import type { PropsWithChildren } from 'react'
import { queryClient } from '@/api/queryClient'
import { system } from '@/theme/system'
import { ColorModeProvider } from './color-mode'

export function Provider({ children }: PropsWithChildren) {
  return (
    <ChakraProvider value={system}>
      <ColorModeProvider>
        <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
      </ColorModeProvider>
    </ChakraProvider>
  )
}

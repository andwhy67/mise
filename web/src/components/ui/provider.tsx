import { ChakraProvider } from '@chakra-ui/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { Suspense, type PropsWithChildren } from 'react'
import { queryClient } from '@/api/queryClient'
import { system } from '@/theme/system'

/** Chakra (примитивы), кэш запросов; Suspense ждёт первый словарь i18n — без мигания ключей вместо текста. */
export function Provider({ children }: PropsWithChildren) {
  return (
    <ChakraProvider value={system}>
      <QueryClientProvider client={queryClient}>
        <Suspense fallback={null}>{children}</Suspense>
      </QueryClientProvider>
    </ChakraProvider>
  )
}

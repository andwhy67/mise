import { EmptyState, VStack } from '@chakra-ui/react'
import type { NavItem } from '@/app/navigation'
import { PageHeader } from '@/components/PageHeader'

/** Заглушка раздела, который появится в следующих фазах. */
export function PlaceholderPage({ item }: { item: NavItem }) {
  const Icon = item.icon
  return (
    <>
      <PageHeader title={item.label} description={item.description} />
      <EmptyState.Root borderWidth="1px" borderStyle="dashed" rounded="xl" bg="bg.panel">
        <EmptyState.Content>
          <EmptyState.Indicator>
            <Icon />
          </EmptyState.Indicator>
          <VStack textAlign="center">
            <EmptyState.Title>Полка пока пустая</EmptyState.Title>
            <EmptyState.Description>Раздел готовится — скоро здесь всё будет на своих местах.</EmptyState.Description>
          </VStack>
        </EmptyState.Content>
      </EmptyState.Root>
    </>
  )
}

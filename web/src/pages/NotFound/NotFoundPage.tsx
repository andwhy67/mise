import { Button, EmptyState, VStack } from '@chakra-ui/react'
import { Link } from 'react-router'
import { LuArrowLeft, LuSearchX } from 'react-icons/lu'

export function NotFoundPage() {
  return (
    <EmptyState.Root minH="60dvh">
      <EmptyState.Content>
        <EmptyState.Indicator>
          <LuSearchX />
        </EmptyState.Indicator>
        <VStack textAlign="center">
          <EmptyState.Title fontFamily="heading" fontSize="3xl">
            Здесь ничего не лежит
          </EmptyState.Title>
          <EmptyState.Description>Проверим другие полки?</EmptyState.Description>
        </VStack>
        <Button asChild colorPalette="brand">
          <Link to="/">
            <LuArrowLeft /> На главную
          </Link>
        </Button>
      </EmptyState.Content>
    </EmptyState.Root>
  )
}

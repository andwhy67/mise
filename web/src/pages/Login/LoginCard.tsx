import { Alert, Box, Button, Collapsible, Field, HStack, Input, Stack, Text } from '@chakra-ui/react'
import { useState, type FormEvent } from 'react'
import { LuLogIn } from 'react-icons/lu'
import { useNavigate } from 'react-router'
import { MiseWordmark } from '@/components/brand/MiseWordmark'
import { ColorModeButton } from '@/components/ui/color-mode'

const demoAccounts = [
  { label: 'Склад', login: 'warehouse' },
  { label: 'Повар', login: 'chef' },
  { label: 'Менеджер', login: 'manager' },
] as const

/** Карточка входа. Реальная авторизация (JWT) — фаза 7; пока только проверка заполненности и переход в приложение. */
export function LoginCard() {
  const navigate = useNavigate()
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!login.trim() || !password) {
      setError('Не получилось войти. Проверьте логин и пароль')
      return
    }
    setError(null)
    navigate('/')
  }

  return (
    <Box w="full" maxW="400px" bg="bg.panel" borderWidth="1px" rounded="2xl" p={{ base: 6, md: 8 }} boxShadow="sm">
      <HStack justify="space-between" mb={8}>
        <MiseWordmark />
        <ColorModeButton />
      </HStack>

      <form onSubmit={handleSubmit} noValidate>
        <Stack gap={4}>
          <Field.Root required invalid={error !== null && !login.trim()}>
            <Field.Label>Логин</Field.Label>
            <Input name="username" autoComplete="username" value={login} onChange={(e) => setLogin(e.target.value)} />
          </Field.Root>
          <Field.Root required invalid={error !== null && !password}>
            <Field.Label>Пароль</Field.Label>
            <Input
              name="password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </Field.Root>

          {error && (
            <Alert.Root status="error" variant="subtle" size="sm">
              <Alert.Indicator />
              <Alert.Title>{error}</Alert.Title>
            </Alert.Root>
          )}

          <Button type="submit" colorPalette="brand" size="lg" mt={2}>
            <LuLogIn /> Войти в смену
          </Button>
        </Stack>
      </form>

      <Collapsible.Root mt={6}>
        <Collapsible.Trigger asChild>
          <Button variant="plain" size="sm" color="fg.muted" px={0}>
            Демо-доступ
          </Button>
        </Collapsible.Trigger>
        <Collapsible.Content>
          <HStack gap={2} pt={2} wrap="wrap">
            {demoAccounts.map((account) => (
              <Button
                key={account.login}
                size="xs"
                variant="outline"
                onClick={() => {
                  setLogin(account.login)
                  setPassword('demo')
                  setError(null)
                }}
              >
                {account.label}
              </Button>
            ))}
          </HStack>
        </Collapsible.Content>
      </Collapsible.Root>

      <Text mt={6} fontSize="xs" color="fg.subtle">
        Mise · v0.1 · кейс «Флагман»
      </Text>
    </Box>
  )
}

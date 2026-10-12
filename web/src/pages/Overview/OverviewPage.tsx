import { Alert, Button, Card, HStack, SimpleGrid, Skeleton, Stack, Status, Text } from '@chakra-ui/react'
import { LuRefreshCw } from 'react-icons/lu'
import { useHealth, type HealthStatus } from '@/api/system'
import { PageHeader } from '@/components/PageHeader'

const dependencyLabels: Record<string, string> = {
  postgres: 'База данных',
  redis: 'Кэш и шина событий',
}

const statusView: Record<HealthStatus, { palette: string; text: string }> = {
  Healthy: { palette: 'green', text: 'Работает' },
  Degraded: { palette: 'orange', text: 'С перебоями' },
  Unhealthy: { palette: 'red', text: 'Недоступно' },
}

function greeting(now = new Date()): string {
  const hour = Number(new Intl.DateTimeFormat('ru-RU', { hour: 'numeric', hourCycle: 'h23', timeZone: 'Europe/Moscow' }).format(now))
  if (hour < 6) return 'Доброй ночи, шеф'
  if (hour < 12) return 'Доброе утро, шеф'
  if (hour < 18) return 'Добрый день, шеф'
  return 'Добрый вечер, шеф'
}

export function OverviewPage() {
  const health = useHealth()

  return (
    <>
      <PageHeader
        title={greeting()}
        description="Здесь появятся показатели смены: стоп-лист, истекающие партии, списания и горячие предложения. Пока проверяем, что все системы на своих местах."
      />

      <Card.Root variant="outline" bg="bg.panel" maxW="2xl">
        <Card.Header>
          <HStack justify="space-between" gap={4}>
            <Card.Title fontSize="lg">Состояние системы</Card.Title>
            <Button size="xs" variant="ghost" onClick={() => void health.refetch()} loading={health.isFetching}>
              <LuRefreshCw /> Обновить
            </Button>
          </HStack>
        </Card.Header>
        <Card.Body>
          {health.isPending && (
            <Stack gap={3} aria-busy="true" aria-label="Загрузка состояния">
              <Skeleton height="24px" />
              <Skeleton height="24px" />
            </Stack>
          )}

          {health.isError && (
            <Alert.Root status="error" variant="subtle">
              <Alert.Indicator />
              <Alert.Content>
                <Alert.Title>Не удалось получить состояние API</Alert.Title>
                <Alert.Description>{health.error.message}. Проверьте, что сервер запущен, и нажмите «Обновить».</Alert.Description>
              </Alert.Content>
            </Alert.Root>
          )}

          {health.isSuccess && (
            <Stack gap={3}>
              <StatusRow label="API" status={health.data.status} detail={`${health.data.totalDurationMs} мс`} />
              {Object.entries(health.data.checks).map(([key, check]) => (
                <StatusRow
                  key={key}
                  label={dependencyLabels[key] ?? key}
                  status={check.status}
                  detail={check.error ?? `${check.durationMs} мс`}
                />
              ))}
            </Stack>
          )}
        </Card.Body>
      </Card.Root>

      <SimpleGrid columns={{ base: 1, sm: 2, lg: 4 }} gap={4} mt={6}>
        {['В стоп-листе', 'Истекает ≤ 24 ч', 'Списано сегодня', 'Заказов сегодня'].map((label) => (
          <Card.Root key={label} variant="subtle" bg="bg.subtle">
            <Card.Body gap={1}>
              <Text fontSize="sm" color="fg.muted">
                {label}
              </Text>
              <Text fontSize="2xl" fontWeight="600" color="fg.subtle" aria-label={`${label}: данных пока нет`}>
                —
              </Text>
            </Card.Body>
          </Card.Root>
        ))}
      </SimpleGrid>
    </>
  )
}

function StatusRow({ label, status, detail }: { label: string; status: HealthStatus; detail: string }) {
  const view = statusView[status]
  return (
    <HStack justify="space-between" gap={4}>
      <Status.Root colorPalette={view.palette}>
        <Status.Indicator />
        <Text as="span" fontWeight="500">
          {label}
        </Text>
      </Status.Root>
      <Text fontSize="sm" color="fg.muted" textAlign="right">
        {view.text} · {detail}
      </Text>
    </HStack>
  )
}

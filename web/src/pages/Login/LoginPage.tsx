import { Box, Flex, Heading, Stack, Text } from '@chakra-ui/react'
import { LoginCard } from './LoginCard'
import { PrepTrayBackground } from './PrepTrayBackground'

export function LoginPage() {
  return (
    <Flex minH="100dvh" direction={{ base: 'column-reverse', lg: 'row' }}>
      {/* Визуал: словарная статья + раскладка лотков */}
      <Flex
        as="section"
        aria-label="О Mise"
        flex="1.2"
        direction="column"
        justify="center"
        gap={10}
        px={{ base: 6, md: 12, xl: 20 }}
        py={{ base: 10, lg: 16 }}
        bg="bg.subtle"
      >
        <Stack gap={4} maxW="520px">
          <Heading as="h1" fontSize={{ base: '5xl', md: '6xl', xl: '7xl' }} fontWeight="600" lineHeight="0.95" letterSpacing="-0.02em">
            Mise en place
          </Heading>
          <Text fontSize="md" color="fg.muted">
            <Text as="span" fontStyle="italic">
              mise en place
            </Text>{' '}
            <Text as="span" fontFamily="mono" fontSize="sm">
              /miz ɑ̃ plas/
            </Text>{' '}
            · фр. «всё на своих местах»
          </Text>
          <Text color="fg.muted" maxW="46ch">
            Принцип профессиональной кухни: до начала смены всё подготовлено, свежее — на виду, ничего не забыто.
          </Text>
        </Stack>
        <Box display={{ base: 'none', sm: 'block' }}>
          <PrepTrayBackground />
        </Box>
      </Flex>

      {/* Форма */}
      <Flex as="main" flex="1" direction="column" align="center" justify="center" gap={6} px={{ base: 4, md: 8 }} py={{ base: 8, lg: 16 }}>
        <LoginCard />
        <Text fontFamily="heading" fontSize="xl" fontStyle="italic" color="fg.muted" textAlign="center">
          Каждая партия на виду. Каждое блюдо — вовремя.
        </Text>
      </Flex>
    </Flex>
  )
}

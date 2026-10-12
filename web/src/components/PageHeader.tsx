import { Heading, Stack, Text } from '@chakra-ui/react'
import type { ReactNode } from 'react'

interface PageHeaderProps {
  title: string
  description?: ReactNode
}

export function PageHeader({ title, description }: PageHeaderProps) {
  return (
    <Stack gap={1} mb={6}>
      <Heading as="h1" fontSize={{ base: '3xl', md: '4xl' }} fontWeight="600" lineHeight="1.1" letterSpacing="-0.01em">
        {title}
      </Heading>
      {description && (
        <Text color="fg.muted" maxW="65ch">
          {description}
        </Text>
      )}
    </Stack>
  )
}

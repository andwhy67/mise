import { Box, HStack, Text } from '@chakra-ui/react'

interface MiseWordmarkProps {
  size?: 'sm' | 'lg'
}

/** Монограмма «M» + wordmark «Mise». */
export function MiseWordmark({ size = 'sm' }: MiseWordmarkProps) {
  const mark = size === 'lg' ? '44px' : '32px'
  return (
    <HStack gap={size === 'lg' ? 3 : 2.5}>
      <Box
        aria-hidden
        display="grid"
        placeItems="center"
        w={mark}
        h={mark}
        rounded="lg"
        bg="brand.solid"
        color="brand.contrast"
        fontFamily="heading"
        fontWeight="700"
        fontSize={size === 'lg' ? '2xl' : 'lg'}
        lineHeight="1"
      >
        M
      </Box>
      <Text fontFamily="heading" fontWeight="600" fontSize={size === 'lg' ? '3xl' : '2xl'} lineHeight="1" letterSpacing="-0.01em">
        Mise
      </Text>
    </HStack>
  )
}

import { Box, HStack, Stack, Text } from '@chakra-ui/react'
import { Link } from 'react-router'
import { LuChevronRight } from 'react-icons/lu'
import { navItems } from '@/app/navigation'
import { PageHeader } from '@/components/PageHeader'

/** Все разделы списком — для нижней навигации на телефоне. */
export function MorePage() {
  const secondary = navItems.filter((item) => !item.primary)
  return (
    <>
      <PageHeader title="Все разделы" />
      <Stack as="ul" gap={2} listStyleType="none" p={0}>
        {secondary.map(({ path, label, icon: Icon, description }) => (
          <Box as="li" key={path}>
            <Link to={path}>
              <HStack
                gap={4}
                p={4}
                rounded="xl"
                borderWidth="1px"
                bg="bg.panel"
                _hover={{ bg: 'bg.subtle' }}
                transition="background-color 120ms ease"
              >
                <Box color="brand.fg" fontSize="xl" aria-hidden>
                  <Icon />
                </Box>
                <Stack gap={0} flex="1" minW={0}>
                  <Text fontWeight="600">{label}</Text>
                  <Text fontSize="sm" color="fg.muted" lineClamp={2}>
                    {description}
                  </Text>
                </Stack>
                <Box color="fg.subtle" aria-hidden>
                  <LuChevronRight />
                </Box>
              </HStack>
            </Link>
          </Box>
        ))}
      </Stack>
    </>
  )
}

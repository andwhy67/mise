import { useTranslation } from 'react-i18next'
import { ApiError } from '@/api/errors'

/** Текст ошибки для человека: по коду из ProblemDetails, иначе общий. */
export function useErrorText() {
  const { t } = useTranslation()
  return (error: unknown) => {
    const code = error instanceof ApiError ? error.code : undefined
    return code && t(`errors.${code}`, { defaultValue: '' }) ? t(`errors.${code}`) : t('errors.unknown')
  }
}

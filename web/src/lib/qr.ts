import QRCode from 'qrcode'
import { useEffect, useState } from 'react'

/**
 * QR-код картинкой (data URL). Всегда тёмный на белом — даже в теме «Графит»: камеры телефонов
 * плохо читают инвертированные коды, а на печати фон и так белый.
 */
export const qrDataUrl = (value: string, size = 320) =>
  QRCode.toDataURL(value, { width: size, margin: 1, errorCorrectionLevel: 'M', color: { dark: '#12263aff', light: '#ffffffff' } })

export function useQrDataUrl(value: string, size = 320): string | null {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    let alive = true
    qrDataUrl(value, size).then(
      (data) => alive && setUrl(data),
      () => alive && setUrl(null),
    )
    return () => {
      alive = false
    }
  }, [value, size])
  return url
}

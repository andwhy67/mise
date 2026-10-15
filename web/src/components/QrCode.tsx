import { useQrDataUrl } from '@/lib/qr'

/** QR-код картинкой; пока рисуется — место того же размера (без скачка вёрстки). */
export function QrCode({ value, label, size = 320, className }: { value: string; label: string; size?: number; className?: string }) {
  const url = useQrDataUrl(value, size)
  return url ? (
    <img src={url} alt={label} width={size} height={size} className={className} />
  ) : (
    <span role="img" aria-label={label} className={className} style={{ display: 'inline-block', width: size, height: size }} />
  )
}

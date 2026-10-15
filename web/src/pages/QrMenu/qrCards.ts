/** QR-карточки: общий код (вход, витрина) и по одной на стол — номер стола приходит на кухню (?t=5). */
export interface QrCard {
  table: number | null
  url: string
}

export const menuUrl = (origin: string, menuKey: string) => `${origin.replace(/\/$/, '')}/m/${menuKey}`

export function qrCards(link: string, tables: number): QrCard[] {
  return [{ table: null, url: link }, ...Array.from({ length: tables }, (_, i) => ({ table: i + 1, url: `${link}?t=${i + 1}` }))]
}

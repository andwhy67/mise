/**
 * CSV для Excel в русской локали: разделитель «;», дробная часть через запятую, UTF-8 с BOM —
 * иначе Excel открывает кириллицу кракозябрами. Несколько таблиц — блоками через пустую строку.
 */
export interface CsvSection {
  title: string
  head: string[]
  rows: (string | number | null)[][]
}

const BOM = String.fromCharCode(0xfeff)
const EOL = '\r\n'
const NEEDS_QUOTES = /[;"\n\r]/
const FORMULA = /^[=+@-]/

function cell(value: string | number | null, decimal: string): string {
  if (value === null) return ''
  const text = typeof value === 'number' ? String(Math.round(value * 1000) / 1000).replace('.', decimal) : value
  // Строка, похожая на формулу (=, +, -, @), — с апострофом: Excel не выполнит её при открытии.
  const safe = typeof value === 'string' && FORMULA.test(text) ? `'${text}` : text
  return NEEDS_QUOTES.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe
}

export function toCsv(sections: CsvSection[], decimal = ','): string {
  const blocks = sections.map((s) =>
    [s.title, s.head.map((h) => cell(h, decimal)).join(';'), ...s.rows.map((r) => r.map((c) => cell(c, decimal)).join(';'))].join(EOL),
  )
  return BOM + blocks.join(EOL + EOL) + EOL
}

/** Скачать текст файлом (без сервера): Blob + временная ссылка. */
export function download(fileName: string, text: string, type = 'text/csv;charset=utf-8') {
  const url = URL.createObjectURL(new Blob([text], { type }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

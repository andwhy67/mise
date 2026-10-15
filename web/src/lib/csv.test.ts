import { describe, expect, it } from 'vitest'
import { toCsv } from './csv'

const CRLF = '\r\n'

describe('CSV для Excel', () => {
  it('BOM, «;», запятая в дробях, блоки через пустую строку', () => {
    const csv = toCsv([
      { title: 'Деньги', head: ['Показатель', 'Сумма'], rows: [['Выручка', 1520.5]] },
      { title: 'Списания', head: ['Продукт', 'Кг'], rows: [['Шпинат', 0.25], ['Соль', null]] },
    ])
    expect(csv.charCodeAt(0)).toBe(0xfeff)
    expect(csv.slice(1)).toBe(['Деньги', 'Показатель;Сумма', 'Выручка;1520,5', '', 'Списания', 'Продукт;Кг', 'Шпинат;0,25', 'Соль;', ''].join(CRLF))
  })

  it('кавычки и разделители внутри значения экранируются, формулы — обезвреживаются', () => {
    const csv = toCsv([{ title: 'T', head: ['a'], rows: [['ООО «Свежесть»; склад'], ['Сыр "Фета"'], ['=HYPERLINK("x")']] }])
    expect(csv).toContain('"ООО «Свежесть»; склад"')
    expect(csv).toContain('"Сыр ""Фета"""')
    expect(csv).toContain(`"'=HYPERLINK(""x"")"`)
  })

  it('для английского интерфейса — точка в дробях', () => {
    expect(toCsv([{ title: 'T', head: ['n'], rows: [[1.5]] }], '.')).toContain('1.5')
  })
})

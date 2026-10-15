import { describe, expect, it } from 'vitest'
import { emptyJoin, emptyRegister, formatCode, isCompleteCode, joinRequest, registerRequest, validateJoin, validateRegister } from './accountForm'

describe('регистрация ресторана', () => {
  it('все поля обязательны; логин и пароль — по правилам сервера', () => {
    expect(validateRegister(emptyRegister())).toEqual({ restaurantName: 'restaurantName', displayName: 'displayName', login: 'login', password: 'password' })
    expect(validateRegister({ restaurantName: 'Бистро', displayName: 'Анна', login: 'Anna.K', password: '12345678', fillDemoCatalog: true })).toEqual({})
    expect(validateRegister({ restaurantName: 'Бистро', displayName: 'Анна', login: 'анна', password: '1234567', fillDemoCatalog: false })).toEqual({
      login: 'login',
      password: 'password',
    })
  })

  it('в запрос — без лишних пробелов, логин в нижнем регистре', () => {
    expect(registerRequest({ restaurantName: ' Бистро ', displayName: ' Анна ', login: ' Anna ', password: ' pass word ', fillDemoCatalog: true })).toEqual({
      restaurantName: 'Бистро',
      displayName: 'Анна',
      login: 'anna',
      password: ' pass word ',
      fillDemoCatalog: true,
    })
  })
})

describe('вход по приглашению', () => {
  it('код вводят как угодно — регистр, пробелы, дефис не важны', () => {
    expect(formatCode('k7q2 pxm4')).toBe('K7Q2-PXM4')
    expect(formatCode('K7Q')).toBe('K7Q')
    expect(formatCode('k7q2-pxm4-extra')).toBe('K7Q2-PXM4')
    expect(isCompleteCode('k7q2-pxm4')).toBe(true)
    expect(isCompleteCode('k7q2-pxm')).toBe(false)
  })

  it('неполный код — ошибка; в запрос уходит код без дефиса', () => {
    expect(validateJoin(emptyJoin('K7Q2'))).toMatchObject({ code: 'code' })
    expect(joinRequest({ code: 'k7q2-pxm4', displayName: 'Пётр', login: 'Cook', password: '12345678' })).toEqual({
      code: 'K7Q2PXM4',
      displayName: 'Пётр',
      login: 'cook',
      password: '12345678',
    })
  })
})

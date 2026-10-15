/**
 * Регистрация ресторана и вход по приглашению — проверка полей до отправки (те же правила, что на сервере):
 * логин 3–32 символа (латиница, цифры, «.», «_», «-»), пароль от 8 символов, код приглашения — 8 знаков.
 */

const LOGIN = /^[a-z0-9._-]{3,32}$/
export const PASSWORD_MIN = 8
const CODE_LENGTH = 8

export interface RegisterForm {
  restaurantName: string
  displayName: string
  login: string
  password: string
  fillDemoCatalog: boolean
}

export interface JoinForm {
  code: string
  displayName: string
  login: string
  password: string
}

export const emptyRegister = (): RegisterForm => ({ restaurantName: '', displayName: '', login: '', password: '', fillDemoCatalog: false })
export const emptyJoin = (code = ''): JoinForm => ({ code, displayName: '', login: '', password: '' })

/** Логин хранится в нижнем регистре: «Anna» и «anna» — один логин. */
export const normalizeLogin = (login: string) => login.trim().toLowerCase()

/** Код как его ввели — без регистра, пробелов и дефисов (сервер нормализует так же). */
export const normalizeCode = (code: string) => code.replace(/[^0-9a-z]/gi, '').toUpperCase()

/** «k7q2pxm4» → «K7Q2-PXM4» — для поля ввода и ссылки. */
export function formatCode(code: string) {
  const plain = normalizeCode(code).slice(0, CODE_LENGTH)
  return plain.length > 4 ? `${plain.slice(0, 4)}-${plain.slice(4)}` : plain
}

export const isCompleteCode = (code: string) => normalizeCode(code).length === CODE_LENGTH

function accountErrors(form: { displayName: string; login: string; password: string }): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!form.displayName.trim()) errors.displayName = 'displayName'
  if (!LOGIN.test(normalizeLogin(form.login))) errors.login = 'login'
  if (form.password.length < PASSWORD_MIN) errors.password = 'password'
  return errors
}

export function validateRegister(form: RegisterForm): Record<string, string> {
  const errors = accountErrors(form)
  if (!form.restaurantName.trim()) errors.restaurantName = 'restaurantName'
  return errors
}

export function validateJoin(form: JoinForm): Record<string, string> {
  const errors = accountErrors(form)
  if (!isCompleteCode(form.code)) errors.code = 'code'
  return errors
}

export const registerRequest = (form: RegisterForm) => ({
  restaurantName: form.restaurantName.trim(),
  displayName: form.displayName.trim(),
  login: normalizeLogin(form.login),
  password: form.password,
  fillDemoCatalog: form.fillDemoCatalog,
})

export const joinRequest = (form: JoinForm) => ({
  code: normalizeCode(form.code),
  displayName: form.displayName.trim(),
  login: normalizeLogin(form.login),
  password: form.password,
})

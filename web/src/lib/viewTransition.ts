/**
 * Плавная смена всего экрана (тема, язык) через View Transitions API. Где API нет или включено
 * `prefers-reduced-motion` — изменение применяется сразу, без анимации.
 */
export type TransitionKind = 'theme' | 'language'

export interface TransitionOrigin {
  x: number
  y: number
}

let current = 0

const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches

/**
 * Даём React закоммитить изменения, чтобы снимок «после» содержал новый текст. Не requestAnimationFrame:
 * пока идёт update-колбэк перехода, отрисовка приостановлена и rAF не срабатывает — переход висел бы до таймаута.
 * Планировщик React работает на макрозадачах, двух оборотов цикла событий достаточно.
 */
const settle = async () => {
  for (let i = 0; i < 2; i++) await new Promise<void>((resolve) => setTimeout(resolve, 0))
}

export async function withViewTransition(kind: TransitionKind, update: () => void | Promise<unknown>, origin?: TransitionOrigin) {
  if (typeof document.startViewTransition !== 'function' || reducedMotion()) {
    await update()
    return
  }

  const root = document.documentElement
  const id = ++current
  root.dataset.transition = kind
  const transition = document.startViewTransition(async () => {
    await update()
    await settle()
  })

  if (kind === 'theme') {
    // Новая тема расходится кругом от кнопки, на которую нажали.
    const x = origin?.x ?? window.innerWidth / 2
    const y = origin?.y ?? 0
    const radius = Math.hypot(Math.max(x, window.innerWidth - x), Math.max(y, window.innerHeight - y))
    transition.ready
      .then(() =>
        root.animate(
          { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
          { duration: 520, easing: 'cubic-bezier(0.2, 0.7, 0.2, 1)', pseudoElement: '::view-transition-new(root)' },
        ),
      )
      // Быстрый второй клик прерывает анимацию — изменение уже применено, ошибка не нужна.
      .catch(() => undefined)
  } else {
    transition.ready.catch(() => undefined)
  }

  try {
    await transition.finished
  } finally {
    // Снимаем метку, только если за это время не началась новая смена.
    if (id === current) delete root.dataset.transition
  }
}

/** Центр элемента, по которому кликнули, — начало круга смены темы. */
export const originOf = (element: Element): TransitionOrigin => {
  const rect = element.getBoundingClientRect()
  return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 }
}

/**
 * Звонок нового заказа — WebAudio, без аудиофайлов: два коротких тона, как на кухонном принтере.
 * Браузер разрешает звук только после действия человека — первое касание экрана «будит» звук (unlockSound).
 */
let context: AudioContext | null = null

export function unlockSound() {
  try {
    context ??= new AudioContext()
    if (context.state === 'suspended') void context.resume()
  } catch {
    // Нет WebAudio (старый браузер) — экран работает без звука.
  }
}

export const soundReady = () => context?.state === 'running'

/** Два тона по 0,18 с с мягкой атакой и затуханием (без щелчка). Звук ещё не разрешён — тихо возвращает false. */
export function chime(): boolean {
  if (!context || context.state !== 'running') return false
  const start = context.currentTime
  ;[880, 1320].forEach((frequency, index) => {
    const at = start + index * 0.2
    const oscillator = context!.createOscillator()
    const gain = context!.createGain()
    oscillator.type = 'sine'
    oscillator.frequency.value = frequency
    gain.gain.setValueAtTime(0.0001, at)
    gain.gain.exponentialRampToValueAtTime(0.35, at + 0.02)
    gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.18)
    oscillator.connect(gain).connect(context!.destination)
    oscillator.start(at)
    oscillator.stop(at + 0.2)
  })
  return true
}

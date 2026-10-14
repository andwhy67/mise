// Тема до загрузки приложения — без белой вспышки в «Графите» (логика переключения: src/theme/colorScheme.ts).
// Отдельный файл, а не inline-скрипт: CSP разрешает только скрипты с того же origin.
try {
  var stored = localStorage.getItem('mise.theme')
  var dark = stored === 'dark' || (stored !== 'light' && matchMedia('(prefers-color-scheme: dark)').matches)
  document.documentElement.classList.add(dark ? 'dark' : 'light')
} catch (e) {}

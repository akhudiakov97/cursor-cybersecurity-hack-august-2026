// Shared light/dark theme handling for both wwwroot/index.html and wwwroot/attack.html.
//
// This must be loaded render-blocking (a plain <script src="/theme.js"></script> in
// <head>, no `defer`/`module`) and run *before* the page paints, so the `.dark` class
// lands on <html> before the first frame. Otherwise the page would flash light for an
// instant even for a visitor whose stored/preferred theme is dark.

const THEME_STORAGE_KEY = 'honeyguard-theme'

const getPreferredTheme = () => {
  const storedTheme = localStorage.getItem(THEME_STORAGE_KEY)
  if (storedTheme === 'light' || storedTheme === 'dark') return storedTheme
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

const applyTheme = (theme) => {
  document.documentElement.classList.toggle('dark', theme === 'dark')
}

applyTheme(getPreferredTheme())

// Exposed as a global (rather than an ES module export) so a plain, render-blocking
// <script src="/theme.js"></script> tag can use it without also needing type="module",
// which would otherwise defer execution until after the page has already painted.
window.handleThemeToggle = () => {
  const nextTheme = document.documentElement.classList.contains('dark') ? 'light' : 'dark'
  localStorage.setItem(THEME_STORAGE_KEY, nextTheme)
  applyTheme(nextTheme)
}

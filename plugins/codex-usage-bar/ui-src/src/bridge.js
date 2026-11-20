// Messages between the page and SettingsWindow.cs (WebView2 postMessage).
//   host → page: settings | quota | usage | window | error
//   page → host: ready | apply | refresh | window | page-error
const webview = window.chrome && window.chrome.webview

export const connected = !!webview

export function post(message) {
  if (webview) webview.postMessage(message)
}

export function subscribe(handler) {
  if (!webview) return () => {}
  const listener = event => handler(event.data)
  webview.addEventListener('message', listener)
  return () => webview.removeEventListener('message', listener)
}

export const DEFAULTS = {
  Alignment: 0, OffsetX: 0, OffsetY: 0, Startup: false, ShowFiveHour: false, ShowWeek: false, Language: '',
}

// Representative numbers used when no session has been read yet (and in a plain browser).
export const SAMPLE_USAGE = {
  status: '跟随当前会话',
  thread: null,
  at: null,
  model: 'gpt-6.2-sol',
  effort: 'xhigh',
  metrics: {
    pinned: false, running: false, rounds: 2, steps: 3, total: 94053, input: 93140, cached: 92674, output: 913,
    speed: 41.6, turnMs: 21940, firstTokenMs: 1830,
  },
}

export const SAMPLE_QUOTA = {
  fiveHour: { remaining: '72%', resets: '11-18 22:37' },
  week: { remaining: '91%', resets: '11-25 19:57' },
}

// Plain-browser preview: behave like a host that already answered.
export const DEMO = !webview && {
  settings: { ...DEFAULTS, ShowFiveHour: true, ShowWeek: true },
  quota: SAMPLE_QUOTA,
  quotaStatus: '更新于 20:29:24 · Codex',
  usage: { ...SAMPLE_USAGE, thread: '回应问候', at: '20:29:24' },
  version: '0.6.2',
}

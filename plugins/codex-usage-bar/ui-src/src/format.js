// Mirrors UsageMetrics in src/Usage.cs so the preview reads exactly like the real capsule.
const oneDecimal = value => String(Math.round(value * 10) / 10)

export function compact(value) {
  if (value == null) return '—'
  if (value >= 1e9) return oneDecimal(value / 1e9) + 'B'
  if (value >= 1e6) return oneDecimal(value / 1e6) + 'M'
  if (value >= 1e3) return oneDecimal(value / 1e3) + 'K'
  return String(value)
}

export const exact = value => (value == null ? '—' : Number(value).toLocaleString('en-US'))

export const hasCache = m => m && m.input != null && m.cached != null && m.cached <= m.input

export const hitRatio = m => (hasCache(m) && m.input > 0 ? m.cached / m.input : null)

export const hitLabel = m => {
  const ratio = hitRatio(m)
  return ratio == null ? '—' : oneDecimal(ratio * 100) + '%'
}

export const speedLabel = m => (m && m.speed != null ? '≈' + oneDecimal(m.speed) : '—')

export const seconds = (ms, unit) => (ms == null ? '—' : String(Math.round(ms / 10) / 100) + ' ' + unit)

// Quota windows arrive as "37.5%" strings; null when unknown.
export function percent(text) {
  const value = parseFloat(text)
  return Number.isFinite(value) ? Math.max(0, Math.min(100, value)) : null
}

// Codex model ids as the composer shows them: "jiji/gpt-6.2-sol" + "xhigh" -> "GPT-6.2 Sol" + " 极高".
const capital = part => (part ? part[0].toUpperCase() + part.slice(1) : part)
export function modelLabel(model, effort, t) {
  const level = effort ? ' ' + t('effort.' + effort, { defaultValue: effort }) : ''
  if (!model) return { name: t('preview.modelFallback'), effort: level }
  const parts = model.split('/').pop().split('-').filter(Boolean)
  const name = /^gpt$/i.test(parts[0]) && parts.length > 1
    ? 'GPT-' + parts[1] + (parts.length > 2 ? ' ' + parts.slice(2).map(capital).join(' ') : '')
    : parts.map(capital).join(' ')
  return { name, effort: level }
}

export const integer = value => (Number.isFinite(Number(value)) ? Math.trunc(Number(value)) : 0)
export const clamp = (value, min, max) => Math.max(min, Math.min(max, value))

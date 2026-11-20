import { useId } from 'react'
import { makeStyles, mergeClasses, Slider, SpinButton } from '@fluentui/react-components'
import { NUMERIC, v } from '../theme.js'
import { clamp, integer } from '../format.js'

// Building blocks of the v3 "precision instrument" language: paper panels split by hairlines,
// tight numerals, rulers and one electric-blue accent.
const useStyles = makeStyles({
  // Sizes in mockup units (932×705 window); the host zooms the page to the real window.
  panel: { backgroundColor: v('paper'), border: `1px solid ${v('hairline')}`, borderRadius: '12px', boxShadow: v('shadow') },
  block: { padding: '18px 20px', '@media (max-width: 759px)': { padding: '14px 14px' } },
  rule: { height: '1px', backgroundColor: v('hairline'), margin: '0 20px', border: 'none', '@media (max-width: 759px)': { margin: '0 14px' } },
  btn: {
    display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: '6px', height: '32px', padding: '0 16px',
    borderRadius: '7px', fontFamily: 'inherit', fontSize: '13px', cursor: 'pointer', whiteSpace: 'nowrap',
    transitionProperty: 'background-color, border-color, color, filter', transitionDuration: '120ms',
    ':focus-visible': { outline: `2px solid ${v('accent')}`, outlineOffset: '2px' },
    ':disabled': { opacity: 0.4, cursor: 'default' },
  },
  primary: { border: 'none', backgroundColor: v('accent'), color: '#fff', ':hover:enabled': { backgroundColor: v('accentHover') } },
  outline: { border: `1px solid ${v('hairline')}`, backgroundColor: v('paper'), color: v('ink'), ':hover:enabled': { filter: 'brightness(0.96)' } },
  link: {
    border: 'none', background: 'transparent', color: v('ink'), height: 'auto', padding: '2px 0', fontSize: '12px', gap: '4px',
    ':hover:enabled': { color: v('accent') },
  },
  small: { height: '28px', padding: '0 10px', fontSize: '12px', borderRadius: '6px' },
  dot: { display: 'inline-block', width: '7px', height: '7px', borderRadius: '50%', flexShrink: 0 },
  ruler: { position: 'relative', height: '12px' },
  tick: { position: 'absolute', top: '2px', width: '1.5px', height: '8px', marginLeft: '-0.75px', borderRadius: '1px' },
  fill: { position: 'absolute', top: '3px', left: 0, height: '6px', borderRadius: '3px', transitionProperty: 'width', transitionDuration: '300ms' },
  segments: { display: 'flex', gap: '5px', height: '24px', alignItems: 'stretch' },
  segment: { flex: 1, borderRadius: '3px', maxWidth: '11px' },
  progress: { height: '4px', borderRadius: '2px', overflow: 'hidden' },
  progressFill: { height: '100%', borderRadius: '2px', transitionProperty: 'width', transitionDuration: '300ms' },
  choice: { display: 'inline-flex', border: `1px solid ${v('hairline')}`, borderRadius: '8px', padding: '3px', gap: '2px', backgroundColor: v('paper') },
  option: {
    display: 'inline-flex', alignItems: 'center', gap: '5px', height: '28px', padding: '0 12px', border: 'none', borderRadius: '6px',
    background: 'transparent', color: v('muted'), fontFamily: 'inherit', fontSize: '13px', cursor: 'pointer',
    ':hover': { color: v('ink'), backgroundColor: v('selected') },
    ':focus-visible': { outline: `2px solid ${v('accent')}`, outlineOffset: '1px' },
  },
  optionOn: { backgroundColor: v('accentSoft'), color: v('accent'), fontWeight: 600, ':hover': { backgroundColor: v('accentSoft'), color: v('accent') } },
  offset: { display: 'flex', flexDirection: 'column', gap: '6px', minWidth: 0 },
  offsetHead: { display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: '8px' },
  offsetValue: { fontFamily: NUMERIC, fontSize: '20px', fontWeight: 600, letterSpacing: '-0.01em', fontVariantNumeric: 'tabular-nums', color: v('ink') },
  offsetRow: { display: 'grid', gridTemplateColumns: '1fr 104px', alignItems: 'center', columnGap: '10px' },
  label: { fontSize: '14px', fontWeight: 600, color: v('ink') },
  hint: { fontSize: '11px', color: v('muted') },
})

export function Panel({ children, className }) {
  return <section className={mergeClasses(useStyles().panel, className)}>{children}</section>
}
export function Block({ children, className }) {
  return <div className={mergeClasses(useStyles().block, className)}>{children}</div>
}
export function Rule() {
  return <hr className={useStyles().rule} />
}

export function Btn({ kind = 'outline', size, className, ...props }) {
  const s = useStyles()
  return <button type="button" className={mergeClasses(s.btn, s[kind], size === 'small' && s.small, className)} {...props} />
}

export function Dot({ tone = 'green' }) {
  return <span className={useStyles().dot} style={{ background: v(tone) }} aria-hidden />
}

// 21 ticks; the filled bar shows a share (0–1) from the left. null leaves the ruler empty.
export function Ruler({ share, ticks = 21, label }) {
  const s = useStyles()
  return (
    <div className={s.ruler} role="img" aria-label={label}>
      {Array.from({ length: ticks }, (_, i) => (
        <span key={i} className={s.tick} style={{ left: `${(i / (ticks - 1)) * 100}%`, background: v('hairline') }} />
      ))}
      {share != null && <span className={s.fill} style={{ width: `${Math.max(1.5, Math.min(100, share * 100))}%`, background: v('accent') }} />}
    </div>
  )
}

// Ten segments; the last `filled` ones are blue.
export function SegmentBar({ filled, total = 10, label }) {
  const s = useStyles()
  return (
    <div className={s.segments} role="img" aria-label={label}>
      {Array.from({ length: total }, (_, i) => (
        <span key={i} className={s.segment} style={{ background: i >= total - filled ? v('accent') : v('ghostLine') }} />
      ))}
    </div>
  )
}

export function Progress({ value, label }) {
  const s = useStyles()
  return (
    <div className={s.progress} style={{ background: v('ghostLine') }} role="progressbar" aria-label={label}
      aria-valuemin={0} aria-valuemax={100} aria-valuenow={value == null ? undefined : Math.round(value * 100)}>
      <div className={s.progressFill} style={{ width: `${(value ?? 0) * 100}%`, background: v('accent') }} />
    </div>
  )
}

export function Choice({ value, options, onChange, label }) {
  const s = useStyles()
  const move = (event, index) => {
    const delta = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
    if (!delta) return
    event.preventDefault()
    const next = (index + delta + options.length) % options.length
    onChange(options[next].value)
    event.currentTarget.parentElement.children[next].focus()
  }
  return (
    <div className={s.choice} role="radiogroup" aria-label={label}>
      {options.map((o, i) => (
        <button key={o.value} type="button" role="radio" aria-checked={value === o.value} tabIndex={value === o.value ? 0 : -1}
          className={mergeClasses(s.option, value === o.value && s.optionOn)}
          onClick={() => onChange(o.value)} onKeyDown={e => move(e, i)}>{o.icon}{o.label}</button>
      ))}
    </div>
  )
}

export function OffsetField({ label, hint, min, max, value, onChange, increase, decrease }) {
  const s = useStyles()
  const id = useId()
  const current = clamp(integer(value), min, max)
  return (
    <div className={s.offset}>
      <div className={s.offsetHead}>
        <label htmlFor={id} className={s.label}>{label}</label>
        <span className={s.offsetValue}>{(current > 0 ? '+' : '') + current}<span className={s.hint}> px</span></span>
      </div>
      <div className={s.offsetRow}>
        <Slider min={min} max={max} value={current} aria-label={label} onChange={(_, d) => onChange(d.value)} />
        <SpinButton id={id} min={min} max={max} value={current} displayValue={`${current} px`}
          incrementButton={{ 'aria-label': increase }} decrementButton={{ 'aria-label': decrease }}
          onChange={(_, d) => {
            const next = d.value ?? parseInt(d.displayValue, 10)
            if (Number.isFinite(next)) onChange(clamp(Math.trunc(next), min, max))
          }} />
      </div>
      <span className={s.hint}>{hint}</span>
    </div>
  )
}

import { forwardRef } from 'react'
import { makeStyles, mergeClasses } from '@fluentui/react-components'
import { CAPSULE } from '../theme.js'
import { compact, hitLabel, speedLabel } from '../format.js'

const useStyles = makeStyles({
  pill: {
    position: 'relative',
    display: 'inline-flex',
    alignItems: 'center',
    boxSizing: 'border-box',
    height: '30px',
    paddingLeft: '22px',
    paddingRight: '10px',
    borderRadius: '10px',
    border: '1px solid',
    fontFamily: "'Segoe UI', 'Microsoft YaHei UI', sans-serif",
    fontSize: '12px',
    lineHeight: '30px',
    whiteSpace: 'nowrap',
    fontVariantNumeric: 'tabular-nums',
    userSelect: 'none',
  },
  dot: { position: 'absolute', left: '10px', top: '12.5px', width: '5px', height: '5px', borderRadius: '50%' },
  divider: { display: 'inline-block', width: '1px', height: '16px', flexShrink: 0 },
  part: { transitionProperty: 'opacity', transitionDuration: '160ms' },
  dim: { opacity: 0.28 },
})

// Same segment order, spacing and colors as Capsule.Fit in src/Capsule.cs.
// The capsule's own labels are Chinese in the real overlay, so they stay Chinese here.
export function buildSegments({ metrics, status, showFiveHour, showWeek, quota, density = 0 }) {
  const parts = []
  let gap = 0
  const add = (text, ink, group) => { parts.push({ text, ink, group, gap }); gap = 0 }
  const space = px => { gap += px }
  const divider = () => { parts.push({ divider: true, gap: gap + 7 }); gap = 7 }
  const quotaOn = showFiveHour || showWeek
  const addQuota = () => {
    divider()
    if (showFiveHour) { add('5h', 'muted', 'quota'); space(4); add(quota?.fiveHour?.remaining ?? '—', 'blue', 'quota') }
    if (showWeek) {
      if (showFiveHour) space(8)
      add('周', 'muted', 'quota'); space(4); add(quota?.week?.remaining ?? '—', 'blue', 'quota')
    }
  }
  if (metrics?.pinned) { add('固定', 'muted', 'session'); space(9) }
  if (!metrics || metrics.total == null) {
    add(status || '等待用量', 'muted', 'status')
    if (quotaOn && density === 0) addQuota()
    return parts
  }
  let session = false
  if (density === 0 && metrics.rounds > 0) { add(`${metrics.rounds} 轮 ${metrics.steps} 步`, 'muted', 'rounds'); session = true }
  if (density <= 1 && metrics.speed != null) {
    if (session) space(9)
    add('↗ ' + speedLabel(metrics), 'blue', 'speed'); space(3); add('tok/s', 'muted', 'speed')
    session = true
  }
  if (session) divider()
  add(compact(metrics.total), 'green', 'tokens'); space(3); add('tok', 'muted', 'tokens')
  if (density <= 2) { space(10); add('缓存', 'muted', 'cache'); space(4); add(hitLabel(metrics), 'text', 'cache') }
  if (quotaOn && density < 4) addQuota()
  return parts
}

export const Capsule = forwardRef(function Capsule({ scheme = 'light', focus, className, style, ...data }, ref) {
  const styles = useStyles()
  const colors = CAPSULE[scheme]
  const parts = buildSegments(data)
  return (
    <span ref={ref} className={mergeClasses(styles.pill, className)} role="img"
      aria-label={parts.filter(p => !p.divider).map(p => p.text).join(' ')}
      style={{ background: colors.fill, borderColor: colors.edge, color: colors.text, ...style }}>
      <span className={styles.dot} style={{ background: colors.dot }} />
      {parts.map((part, index) => part.divider
        ? <span key={index} className={styles.divider} style={{ marginLeft: part.gap, background: colors.edge }} />
        : <span key={index} data-group={part.group} className={mergeClasses(styles.part, focus && part.group !== focus && styles.dim)}
            style={{ marginLeft: part.gap, color: colors[part.ink] }}>{part.text}</span>)}
    </span>
  )
})

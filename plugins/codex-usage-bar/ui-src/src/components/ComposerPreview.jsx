import { useLayoutEffect, useRef, useState } from 'react'
import { makeStyles, mergeClasses } from '@fluentui/react-components'
import { AddRegular, ChevronDownRegular, MicRegular, ShieldCheckmarkRegular } from '@fluentui/react-icons'
import { useTranslation } from 'react-i18next'
import { Capsule } from './Capsule.jsx'
import { NUMERIC, v } from '../theme.js'
import { integer, modelLabel } from '../format.js'
import { offsetFor, place, STRIP, STRIP_WIDTH } from '../geometry.js'

// The Codex desktop composer, laid out in DIPs at 100 % and scaled to fit, so the capsule keeps
// its true size and position relative to the buttons around it (see the Codex screenshot).
const W = 1000, H = 128, ROW = 96 // row centre line
const RING = STRIP.right + 9, MODEL = RING + 30

// Colors sampled from the Codex composer (light / dark).
const CODEX = {
  light: { box: '#ffffff', border: '#e7e7e7', placeholder: '#a8a8a8', ink: '#1f1f1f', sub: '#6e6e6e', ring: '#d9d9d9', ringArc: '#8c8c8c', voice: '#3a6fd8', shadow: '0 2px 10px rgba(0,0,0,.06)' },
  dark: { box: '#2b2b2b', border: '#3a3a3a', placeholder: '#8a8a8a', ink: '#ececec', sub: '#a8a8a8', ring: '#474747', ringArc: '#a0a0a0', voice: '#4d7ff0', shadow: '0 2px 10px rgba(0,0,0,.3)' },
}

const useStyles = makeStyles({
  frame: { position: 'relative', borderRadius: '10px', padding: '12px 14px 4px', '@media (max-width: 759px)': { padding: '10px 8px 4px' } },
  stage: { position: 'relative' },
  canvas: { position: 'absolute', left: 0, top: 0, width: `${W}px`, height: `${H}px`, transformOrigin: '0 0' },
  box: { position: 'absolute', inset: 0, borderRadius: '26px', border: '1px solid', boxSizing: 'border-box' },
  placeholder: { position: 'absolute', left: '22px', top: '22px', fontSize: '17px' },
  control: { position: 'absolute', top: `${ROW - 14}px`, height: '28px', display: 'flex', alignItems: 'center', gap: '7px', fontSize: '17px', whiteSpace: 'nowrap' },
  ring: { position: 'absolute', left: `${RING}px`, top: `${ROW - 9}px`, width: '18px', height: '18px', boxSizing: 'border-box', borderRadius: '50%', border: '2px solid' },
  voice: { position: 'absolute', top: `${ROW - 20}px`, width: '40px', height: '40px', borderRadius: '50%', display: 'grid', placeItems: 'center' },
  guide: { position: 'absolute', left: `${STRIP.left - 4}px`, width: `${STRIP_WIDTH + 8}px`, top: `${ROW - 21}px`, height: '42px', borderRadius: '12px', pointerEvents: 'none' },
  capsule: { position: 'absolute', transitionProperty: 'left, top', transitionDuration: '160ms', touchAction: 'none' },
  draggable: { cursor: 'grab', ':active': { cursor: 'grabbing' }, ':focus-visible': { outline: 'none' } },
  still: { transitionDuration: '0ms' },
  dash: { position: 'absolute', inset: '-5px', borderRadius: '13px', pointerEvents: 'none' },
  dimension: { position: 'absolute', height: '30px', pointerEvents: 'none' },
  dimLabel: { position: 'absolute', top: '9px', left: '50%', transform: 'translateX(-50%)', fontFamily: NUMERIC, fontSize: '11px', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' },
})

function Waveform() {
  return (
    <svg width="18" height="16" viewBox="0 0 18 16" aria-hidden>
      {[[1, 6], [5, 12], [9, 16], [13, 10], [17, 5]].map(([x, h]) => (
        <rect key={x} x={x - 1} y={(16 - h) / 2} width="2" height={h} rx="1" fill="#fff" />
      ))}
    </svg>
  )
}

export function ComposerPreview({ settings, usage, quota, scheme, guides = false, change }) {
  const { t } = useTranslation()
  const s = useStyles()
  const c = CODEX[scheme]
  const stage = useRef(null), pill = useRef(null), drag = useRef(null)
  const [size, setSize] = useState({ stage: W, pill: 380 })
  const [dragging, setDragging] = useState(false)
  useLayoutEffect(() => {
    const measure = () => stage.current && pill.current && setSize(prev => {
      const next = { stage: stage.current.clientWidth, pill: pill.current.offsetWidth }
      return prev.stage === next.stage && prev.pill === next.pill ? prev : next
    })
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(stage.current)
    observer.observe(pill.current)
    return () => observer.disconnect()
  }, [])

  const scale = Math.min(1, size.stage / W)
  const width = size.pill
  const { x } = place(settings.Alignment, settings.OffsetX, width)
  const y = ROW - 15 + integer(settings.OffsetY)
  const editable = !!change
  const model = modelLabel(usage?.model, usage?.effort, t)

  const onMove = event => {
    if (!drag.current) return
    change('OffsetX', offsetFor(settings.Alignment, drag.current.x + (event.clientX - drag.current.client) / scale, width))
  }
  const onKey = event => {
    const step = event.shiftKey ? 10 : 1
    const target = event.key === 'ArrowLeft' ? x - step : event.key === 'ArrowRight' ? x + step : null
    if (target == null) return
    event.preventDefault()
    change('OffsetX', offsetFor(settings.Alignment, target, width))
  }

  return (
    <div className={s.frame} style={{ background: v('ghost') }} aria-label={t('preview.label')}>
      <div ref={stage} className={s.stage} style={{ height: H * scale + 28 }}>
        <div className={s.canvas} style={{ transform: `scale(${scale})` }}>
          <div className={s.box} style={{ background: c.box, borderColor: c.border, boxShadow: c.shadow }} />
          <span className={s.placeholder} style={{ color: c.placeholder }}>{t('preview.placeholder')}</span>
          <span className={s.control} style={{ left: 18, color: c.ink }}><AddRegular fontSize={24} /></span>
          <span className={s.control} style={{ left: 56, color: c.sub }}><ShieldCheckmarkRegular fontSize={20} />{t('preview.approve')}</span>
          {guides && <span className={s.guide} style={{ border: `1px dashed ${v('faint')}` }} />}
          <div className={mergeClasses(s.capsule, editable && s.draggable, dragging && s.still)} style={{ left: x, top: y }}
            {...(editable ? {
              role: 'slider', tabIndex: 0, 'aria-label': t('rail.drag'),
              'aria-valuemin': -240, 'aria-valuemax': 240, 'aria-valuenow': integer(settings.OffsetX),
              onPointerDown: e => { e.currentTarget.setPointerCapture(e.pointerId); drag.current = { client: e.clientX, x }; setDragging(true) },
              onPointerMove: onMove,
              onPointerUp: () => { drag.current = null; setDragging(false) },
              onPointerCancel: () => { drag.current = null; setDragging(false) },
              onKeyDown: onKey,
            } : {})}>
            {guides && <span className={s.dash} style={{ border: `1.5px dashed ${v('accent')}` }} />}
            <Capsule ref={pill} scheme={scheme} metrics={usage?.metrics} status={usage?.status} quota={quota}
              showFiveHour={settings.ShowFiveHour} showWeek={settings.ShowWeek} />
          </div>
          <span className={s.ring} style={{ borderColor: c.ring, borderTopColor: c.ringArc, borderRightColor: c.ringArc }} />
          <span className={s.control} style={{ left: MODEL, color: c.ink }}>
            {model.name}{model.effort && <span style={{ color: c.sub }}>{model.effort}</span>}
            <ChevronDownRegular fontSize={16} style={{ color: c.sub }} />
          </span>
          <span className={s.control} style={{ left: W - 104, color: c.ink }}><MicRegular fontSize={24} /></span>
          <span className={s.voice} style={{ left: W - 60, background: c.voice }}><Waveform /></span>
        </div>
        <div className={s.dimension} style={{ left: x * scale, width: width * scale, top: H * scale + 4 }} aria-hidden>
          <svg width={width * scale} height="10" style={{ position: 'absolute', top: 0, left: 0, overflow: 'visible' }}>
            <g stroke={v('muted')} strokeWidth="1" fill="none">
              <path d={`M0 0 V9 M${width * scale} 0 V9`} />
              <path d={`M1 5 H${width * scale - 1}`} strokeDasharray="4 3" />
              <path d={`M6 2 L1 5 L6 8 M${width * scale - 6} 2 L${width * scale - 1} 5 L${width * scale - 6} 8`} />
            </g>
          </svg>
          <span className={s.dimLabel} style={{ color: v('ink') }}>{t('preview.dimension', { w: Math.round(width), h: 30 })}</span>
        </div>
      </div>
    </div>
  )
}

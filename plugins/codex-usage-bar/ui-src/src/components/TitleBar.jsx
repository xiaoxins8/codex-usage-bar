import { useRef } from 'react'
import { makeStyles, mergeClasses } from '@fluentui/react-components'
import { useTranslation } from 'react-i18next'
import { v } from '../theme.js'

// Window caption drawn by the page (v3 design). The host moves the window on "drag" and
// handles minimize / maximize / close; Windows snapping and Alt+Space keep working natively.
const GLYPHS = "'Segoe Fluent Icons', 'Segoe MDL2 Assets'"

const useStyles = makeStyles({
  // Sizes in mockup units (932×705 window); the host zooms the page to the real window.
  bar: {
    display: 'flex', alignItems: 'center', height: '38px', flexShrink: 0, paddingLeft: '13px', userSelect: 'none',
    backgroundColor: v('window'), borderBottom: `1px solid ${v('hairline')}`,
  },
  logo: { width: '16px', height: '16px', borderRadius: '4px', display: 'grid', placeItems: 'center', flexShrink: 0 },
  name: { fontSize: '13px', fontWeight: 600, marginLeft: '8px', whiteSpace: 'nowrap', color: v('ink') },
  sub: { fontSize: '12px', marginLeft: '8px', color: v('muted'), whiteSpace: 'nowrap' },
  spacer: { flex: 1, alignSelf: 'stretch' },
  button: {
    width: '40px', height: '38px', display: 'grid', placeItems: 'center', border: 'none', background: 'transparent', padding: 0,
    fontFamily: GLYPHS, fontSize: '9px', color: v('ink'), cursor: 'default', transitionProperty: 'background-color', transitionDuration: '80ms',
    ':hover': { backgroundColor: v('selected') },
    ':focus-visible': { outline: `2px solid ${v('accent')}`, outlineOffset: '-2px' },
  },
  close: { ':hover': { backgroundColor: '#C42B1C', color: '#fff' } },
})

export function TitleBar({ maximized, onCommand }) {
  const { t } = useTranslation()
  const s = useStyles()
  const lastDown = useRef(0)
  const down = event => {
    if (event.button !== 0 || event.target.closest('button')) return
    const now = performance.now()
    // The move loop swallows the native double-click, so detect it here.
    if (now - lastDown.current < 450) { lastDown.current = 0; onCommand('maximize'); return }
    lastDown.current = now
    onCommand('drag')
  }
  return (
    <div className={s.bar} onMouseDown={down}>
      <span className={s.logo} style={{ background: v('accent') }} aria-hidden>
        <svg width="10" height="8" viewBox="0 0 12 10">
          {[[1, 4], [5, 6.5], [9, 9]].map(([x, h]) => <rect key={x} x={x} y={10 - h} width="2.2" height={h} rx="0.8" fill="#fff" />)}
        </svg>
      </span>
      <span className={s.name}>{t('app.name')}</span>
      <span className={s.sub}>{t('app.tagline')}</span>
      <span className={s.spacer} />
      <button type="button" className={s.button} aria-label={t('title.minimize')} title={t('title.minimize')}
        onClick={() => onCommand('minimize')}>{''}</button>
      <button type="button" className={s.button} aria-label={maximized ? t('title.restore') : t('title.maximize')}
        title={maximized ? t('title.restore') : t('title.maximize')} onClick={() => onCommand('maximize')}>{maximized ? '' : ''}</button>
      <button type="button" className={mergeClasses(s.button, s.close)} aria-label={t('title.close')} title={t('title.close')}
        onClick={() => onCommand('close')}>{''}</button>
    </div>
  )
}

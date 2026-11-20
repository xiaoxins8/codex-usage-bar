import { useCallback, useEffect, useRef, useState } from 'react'
import { FluentProvider, makeStyles, mergeClasses, Spinner, Toast, Toaster, ToastTitle, useId, useToastController } from '@fluentui/react-components'
import {
  bundleIcon, DocumentText24Filled, DocumentText24Regular, Person24Filled, Person24Regular,
  Settings24Filled, Settings24Regular, SlideLayout24Filled, SlideLayout24Regular,
} from '@fluentui/react-icons'
import { useTranslation } from 'react-i18next'
import { connected, DEFAULTS, DEMO, post, SAMPLE_USAGE, subscribe } from './bridge.js'
import { applyLanguage, noticeKey, statusKey } from './i18n.js'
import { cssVars, darkTheme, FONT, lightTheme, NUMERIC, useColorScheme, v } from './theme.js'
import { clamp, integer } from './format.js'
import { Btn, Dot } from './components/kit.jsx'
import { TitleBar } from './components/TitleBar.jsx'
import { Overview } from './pages/Overview.jsx'
import { Placement } from './pages/Placement.jsx'
import { Quota } from './pages/Quota.jsx'
import { General } from './pages/General.jsx'

const PAGES = [
  { id: 'overview', icon: bundleIcon(DocumentText24Filled, DocumentText24Regular), page: Overview },
  { id: 'placement', icon: bundleIcon(SlideLayout24Filled, SlideLayout24Regular), page: Placement },
  { id: 'quota', icon: bundleIcon(Person24Filled, Person24Regular), page: Quota },
  { id: 'general', icon: bundleIcon(Settings24Filled, Settings24Regular), page: General },
]
const NARROW = '@media (max-width: 759px)'
const STALE_MS = 3000 // readings arrive about once a second while Codex is in front

const useStyles = makeStyles({
  root: { height: '100vh', display: 'flex', flexDirection: 'column', fontFamily: FONT, color: v('ink'), backgroundColor: v('window') },
  // Sizes in mockup units (932×705 window); the host zooms the page to the real window.
  body: { flex: 1, minHeight: 0, display: 'grid', gridTemplateColumns: '203px minmax(0, 1fr)', [NARROW]: { gridTemplateColumns: 'minmax(0, 1fr)' } },
  side: { display: 'flex', flexDirection: 'column', minHeight: 0, backgroundColor: v('window'), borderRight: `1px solid ${v('hairline')}`, [NARROW]: { display: 'none' } },
  brand: { display: 'flex', alignItems: 'center', gap: '12px', padding: '20px 16px 0' },
  logo: { width: '36px', height: '36px', borderRadius: '9px', flexShrink: 0, display: 'grid', placeItems: 'center' },
  brandName: { fontSize: '16px', lineHeight: '20px', fontWeight: 600, whiteSpace: 'nowrap' },
  brandSub: { fontSize: '11px', color: v('muted'), marginTop: '1px' },
  nav: { display: 'flex', flexDirection: 'column', gap: '5px', marginTop: '23px' },
  item: {
    position: 'relative', display: 'flex', alignItems: 'center', gap: '10px', height: '34px', margin: '0 12px 0 17px', padding: '0 12px',
    border: 'none', borderRadius: '8px', background: 'transparent', color: v('ink'), fontFamily: 'inherit', fontSize: '14px',
    cursor: 'pointer', textAlign: 'left',
    ':hover': { backgroundColor: v('selected') },
    ':focus-visible': { outline: `2px solid ${v('accent')}`, outlineOffset: '2px' },
  },
  itemOn: {
    backgroundColor: v('selected'), fontWeight: 600,
    '::before': { content: '""', position: 'absolute', left: '-17px', top: '3px', bottom: '3px', width: '3px', borderRadius: '0 2px 2px 0', backgroundColor: v('accent') },
  },
  watch: { margin: 'auto 16px 0', padding: '13px 0 18px', borderTop: `1px solid ${v('hairline')}`, display: 'flex', flexDirection: 'column', gap: '1px' },
  watchHead: { display: 'flex', alignItems: 'center', gap: '7px', fontSize: '13px', fontWeight: 600, marginBottom: '3px' },
  watchLine: { fontSize: '11px', color: v('muted'), overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', lineHeight: '16px' },
  main: { display: 'flex', flexDirection: 'column', minWidth: 0, minHeight: 0, backgroundColor: v('main') },
  // Stable gutter: a scrollbar appearing must not change the width (and the preview scale) and loop.
  scroll: { flex: 1, minHeight: 0, overflowY: 'auto', scrollbarGutter: 'stable' },
  inner: { padding: '18px 16px 6px', [NARROW]: { padding: '14px 10px 12px' } },
  header: { display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) auto', alignItems: 'start', columnGap: '12px', margin: '0 6px 12px' },
  title: { fontSize: '24px', lineHeight: '30px', fontWeight: 600, margin: 0, letterSpacing: '-0.01em' },
  subtitle: { fontSize: '13px', color: v('muted'), marginTop: '4px', lineHeight: '20px' },
  pill: {
    display: 'inline-flex', alignItems: 'center', gap: '6px', height: '26px', padding: '0 10px', borderRadius: '13px', whiteSpace: 'nowrap',
    backgroundColor: v('paper'), border: `1px solid ${v('hairline')}`, fontSize: '12px', fontFamily: NUMERIC, marginTop: '2px',
  },
  tabs: { display: 'none', gap: '6px', overflowX: 'auto', marginBottom: '14px', [NARROW]: { display: 'flex' } },
  tab: { border: `1px solid ${v('hairline')}`, background: v('paper'), borderRadius: '17px', height: '34px', padding: '0 14px', fontFamily: 'inherit', fontSize: '14px', color: v('ink'), whiteSpace: 'nowrap', cursor: 'pointer' },
  tabOn: { backgroundColor: v('accentSoft'), borderColor: v('accent'), color: v('accent'), fontWeight: 600 },
  bar: {
    display: 'flex', alignItems: 'center', gap: '10px', minHeight: '56px', padding: '0 20px 0 22px', flexShrink: 0, boxSizing: 'border-box',
    borderTop: `1px solid ${v('hairline')}`, backgroundColor: v('main'), [NARROW]: { padding: '0 10px', gap: '8px' },
  },
  barStatus: { flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', whiteSpace: 'nowrap', overflow: 'hidden' },
  hint: { fontSize: '11px', color: v('faint'), [NARROW]: { display: 'none' } },
  loading: { gridColumn: '1 / -1', display: 'grid', placeItems: 'center' },
})

function Logo() {
  const s = useStyles()
  return (
    <span className={s.logo} style={{ background: v('accent') }} aria-hidden>
      <svg width="19" height="16" viewBox="0 0 28 24" fill="none">
        <rect x="1" y="1" width="26" height="22" rx="4" stroke="#fff" strokeWidth="2" />
        {[[7, 13], [11, 10], [15, 7], [19, 4]].map(([x, h]) => <rect key={x} x={x} y={19 - h} width="2.6" height={h} rx="1" fill="#fff" />)}
      </svg>
    </span>
  )
}

const normalize = s => ({ ...s, OffsetX: clamp(integer(s.OffsetX), -240, 240), OffsetY: clamp(integer(s.OffsetY), -32, 24) })

export function App() {
  const { t } = useTranslation()
  const s = useStyles()
  const scheme = useColorScheme()
  const toasterId = useId('toaster')
  const { dispatchToast } = useToastController(toasterId)
  const initial = DEMO ? DEMO.settings : DEFAULTS
  const [settings, setSettings] = useState(initial)
  const [saved, setSaved] = useState(initial)
  const [host, setHost] = useState({ ready: !connected, busy: false, startupAvailable: true, version: DEMO ? DEMO.version : '', frame: true, maximized: false })
  const [quota, setQuota] = useState({ data: DEMO ? DEMO.quota : null, status: DEMO ? DEMO.quotaStatus : '' })
  const [usage, setUsage] = useState(DEMO ? DEMO.usage : null)
  const [receivedAt, setReceivedAt] = useState(0)
  const [now, setNow] = useState(Date.now())
  const [page, setPage] = useState(() => { try { return sessionStorage.getItem('page') || 'overview' } catch { return 'overview' } })
  const scroller = useRef(null)

  const toast = useCallback((text, intent) => dispatchToast(
    <Toast><ToastTitle>{text}</ToastTitle></Toast>, { intent, position: 'top-end', timeout: 3200 }), [dispatchToast])
  const localize = useCallback(text => (noticeKey(text) ? t(noticeKey(text)) : text), [t])

  useEffect(() => subscribe(data => {
    if (data.type === 'settings') {
      const value = { ...DEFAULTS, ...data.settings }
      setSettings(value)
      setSaved(value)
      setHost(h => ({ ...h, ready: true, busy: false, startupAvailable: data.startupAvailable, version: data.version || h.version,
        frame: data.frame !== false, maximized: !!data.maximized }))
      if (data.notice) toast(localize(data.notice), noticeKey(data.notice) === 'notice.saved' ? 'success' : 'warning')
    } else if (data.type === 'quota') {
      setQuota({ data: data.quota, status: data.status || '' })
    } else if (data.type === 'usage') {
      setUsage(data)
      setReceivedAt(Date.now())
    } else if (data.type === 'window') {
      setHost(h => ({ ...h, maximized: !!data.maximized }))
    } else if (data.type === 'error') {
      setHost(h => ({ ...h, busy: false }))
      toast(localize(data.notice), 'error')
    }
  }), [toast, localize])
  useEffect(() => { post({ type: 'ready' }) }, [])
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer) }, [])
  useEffect(() => { applyLanguage(settings.Language) }, [settings.Language])
  useEffect(() => {
    try { sessionStorage.setItem('page', page) } catch { /* storage unavailable */ }
    if (scroller.current) scroller.current.scrollTop = 0
  }, [page])

  const change = useCallback((key, value) => setSettings(previous => ({ ...previous, [key]: value })), [])
  const normalized = normalize(settings)
  const changes = Object.keys(DEFAULTS).filter(key => normalized[key] !== saved[key]).length
  const apply = useCallback(() => {
    if (!host.ready || host.busy || !changes) return
    if (connected) {
      setHost(h => ({ ...h, busy: true }))
      post({ type: 'apply', settings: normalized })
    } else {
      setSettings(normalized)
      setSaved(normalized)
      toast(t('notice.previewApplied'), 'success')
    }
  }, [host.ready, host.busy, changes, normalized, toast, t])
  useEffect(() => {
    const onKey = event => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') { event.preventDefault(); apply() }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [apply])

  // Always show a complete capsule: live numbers when a session was read, otherwise samples.
  const live = connected && !!(usage && usage.metrics)
  const fresh = live && now - receivedAt < STALE_MS
  const shownUsage = live ? usage : { ...SAMPLE_USAGE, status: usage?.status ?? SAMPLE_USAGE.status }
  const shownQuota = quota.data
  const statusText = usage?.status ? (statusKey(usage.status) ? t(statusKey(usage.status)) : usage.status) : t('live.waiting')
  const Page = (PAGES.find(p => p.id === page) || PAGES[0]).page
  const pill = !connected ? [t('top.sample'), 'faint'] : fresh ? [t('top.live'), 'accent'] : live ? [t('top.paused'), 'amber'] : [statusText, 'amber']
  const props = {
    settings: normalized, saved, change, usage: shownUsage, live, quota: shownQuota, quotaStatus: quota.status, scheme,
    go: setPage, startupAvailable: host.startupAvailable, version: host.version, refresh: () => post({ type: 'refresh' }),
  }

  return (
    // Layout styles live on an inner div: Fluent copies the provider's classes onto popup portals.
    <FluentProvider theme={scheme === 'dark' ? darkTheme : lightTheme}>
      <div className={s.root} style={cssVars(scheme)}>
        {host.frame && <TitleBar maximized={host.maximized} onCommand={action => post({ type: 'window', action })} />}
        <div className={s.body}>
        {!host.ready ? (
          <div className={s.loading} style={{ background: v('main') }}><Spinner label={t('app.loading')} /></div>
        ) : (<>
          <aside className={s.side} aria-label={t('nav.label')}>
            <div className={s.brand}>
              <Logo />
              <div style={{ minWidth: 0 }}>
                <div className={s.brandName}>{t('app.name')}</div>
                <div className={s.brandSub}>{t('app.tagline')}</div>
              </div>
            </div>
            <nav className={s.nav}>
              {PAGES.map(({ id, icon: Icon }) => (
                <button key={id} type="button" className={mergeClasses(s.item, page === id && s.itemOn)}
                  aria-current={page === id ? 'page' : undefined} onClick={() => setPage(id)}>
                  <Icon filled={page === id} fontSize={17} style={{ color: page === id ? v('accent') : v('muted') }} />{t('nav.' + id)}
                </button>
              ))}
            </nav>
            <div className={s.watch} aria-live="polite">
              <div className={s.watchHead}>
                <Dot tone={!connected ? 'faint' : fresh ? 'green' : 'amber'} />
                {!connected ? t('live.demo') : live ? t('side.listening') : statusText}
              </div>
              <div className={s.watchLine} title={usage?.thread || ''}>{live && usage.thread ? usage.thread : t('live.noSession')}</div>
              {fresh
                ? <div className={s.watchLine}>{t('live.updatedAt', { time: usage.at })}</div>
                : <><div className={s.watchLine}>{t('side.away')}</div><div className={s.watchLine}>{t('side.comeBack')}</div></>}
              <div className={s.watchLine}>{t('app.version', { version: host.version || '—' })}</div>
            </div>
          </aside>

          <div className={s.main}>
            <div className={s.scroll} ref={scroller}>
              <div className={s.inner}>
                <div className={s.tabs}>
                  {PAGES.map(({ id }) => (
                    <button key={id} type="button" className={mergeClasses(s.tab, page === id && s.tabOn)} onClick={() => setPage(id)}>{t('nav.' + id)}</button>
                  ))}
                </div>
                <header className={s.header}>
                  <div>
                    <h1 className={s.title}>{t('nav.' + page)}</h1>
                    <div className={s.subtitle}>{t('page.' + page)}</div>
                  </div>
                  <span className={s.pill}><Dot tone={pill[1]} />{pill[0]}</span>
                </header>
                <Page {...props} />
              </div>
            </div>
            <footer className={s.bar}>
              <div className={s.barStatus} role="status">
                <Dot tone={changes ? 'amber' : 'green'} />
                <span style={{ color: changes ? v('ink') : v('green') }}>
                  {!connected ? t('actions.previewOnly') : changes ? t('actions.dirty', { count: changes }) : t('actions.clean')}
                </span>
                {changes > 0 && <span className={s.hint}>{t('actions.shortcut')}</span>}
              </div>
              <Btn kind="outline" disabled={!changes || host.busy} onClick={() => setSettings(saved)}>{t('actions.discard')}</Btn>
              <Btn kind="primary" disabled={!changes || host.busy} onClick={apply}>
                {host.busy && <Spinner size="tiny" appearance="inverted" />}{host.busy ? t('actions.applying') : t('actions.apply')}
              </Btn>
            </footer>
          </div>
        </>)}
        </div>
      </div>
      <Toaster toasterId={toasterId} />
    </FluentProvider>
  )
}

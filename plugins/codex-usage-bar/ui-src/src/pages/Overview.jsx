import { makeStyles } from '@fluentui/react-components'
import { ArrowRight16Regular } from '@fluentui/react-icons'
import { useTranslation } from 'react-i18next'
import { Block, Btn, Panel, Progress, Rule, Ruler, SegmentBar } from '../components/kit.jsx'
import { ComposerPreview } from '../components/ComposerPreview.jsx'
import { NUMERIC, v } from '../theme.js'
import { compact, exact, hasCache, hitLabel, hitRatio, percent } from '../format.js'

const useStyles = makeStyles({
  // Sizes in mockup units (932×705 window); the host zooms the page to the real window.
  head: { display: 'flex', alignItems: 'baseline', gap: '12px', flexWrap: 'wrap' },
  kicker: { fontSize: '14px', fontWeight: 600, letterSpacing: '0.04em', color: v('ink') },
  meta: { fontSize: '14px', color: v('muted'), fontFamily: NUMERIC, fontVariantNumeric: 'tabular-nums' },
  reading: { display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) auto', alignItems: 'start', columnGap: '16px' },
  big: { display: 'flex', alignItems: 'baseline', gap: '8px', marginTop: '4px', fontFamily: NUMERIC, color: v('ink'), whiteSpace: 'nowrap' },
  bigNumber: { fontSize: '40px', lineHeight: '46px', fontWeight: 600, letterSpacing: '-0.03em', fontVariantNumeric: 'tabular-nums' },
  bigUnit: { fontSize: '18px', fontWeight: 400, color: v('ink') },
  side: { display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '10px', paddingTop: '2px' },
  sideItem: { display: 'flex', flexDirection: 'column', alignItems: 'flex-end' },
  sideValue: { fontFamily: NUMERIC, fontSize: '18px', lineHeight: '22px', fontWeight: 600, letterSpacing: '-0.01em', fontVariantNumeric: 'tabular-nums', color: v('ink'), whiteSpace: 'nowrap' },
  caption: { fontSize: '11px', color: v('muted'), letterSpacing: '0.02em', marginTop: '1px' },
  note: { fontSize: '11px', color: v('muted'), marginTop: '3px', fontFamily: NUMERIC, fontVariantNumeric: 'tabular-nums' },
  ruler: { marginTop: '14px' },
  rulerLabels: { display: 'flex', justifyContent: 'space-between', gap: '12px', marginTop: '4px', fontSize: '10px', lineHeight: '13px', color: v('muted'), fontFamily: NUMERIC, fontVariantNumeric: 'tabular-nums' },
  sessionBlock: { paddingTop: '16px', paddingBottom: '13px' },
  positionBlock: { paddingTop: '13px', paddingBottom: '12px' },
  sectionHead: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', marginBottom: '8px' },
  title: { fontSize: '15px', fontWeight: 600, color: v('ink') },
  ledger: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', overflow: 'hidden' },
  column: { display: 'flex', flexDirection: 'column', gap: '2px', padding: '12px 20px 10px', margin: '0 0 0 -1px', borderLeft: `1px solid ${v('hairline')}`, minWidth: 0 },
  colTitle: { fontSize: '14px', fontWeight: 600, color: v('ink') },
  colSub: { fontSize: '11px', color: v('muted'), minHeight: '15px' },
  colValue: { display: 'flex', alignItems: 'baseline', gap: '6px', margin: '6px 0 8px', fontFamily: NUMERIC, color: v('ink'), whiteSpace: 'nowrap' },
  colNumber: { fontSize: '24px', lineHeight: '30px', fontWeight: 600, letterSpacing: '-0.02em', fontVariantNumeric: 'tabular-nums' },
  colUnit: { fontSize: '14px' },
  colFoot: { fontSize: '11px', color: v('muted'), marginTop: '6px', fontFamily: NUMERIC, fontVariantNumeric: 'tabular-nums' },
})

export function Overview({ settings, usage, live, quota, scheme, go }) {
  const { t } = useTranslation()
  const s = useStyles()
  const m = usage.metrics
  const quotaOn = settings.ShowFiveHour || settings.ShowWeek
  const fiveLeft = percent(quota?.fiveHour?.remaining), weekLeft = percent(quota?.week?.remaining)
  const rulerUsed = settings.ShowFiveHour && fiveLeft != null ? (100 - fiveLeft) / 100 : settings.ShowWeek && weekLeft != null ? (100 - weekLeft) / 100 : null
  // Share of tokens not served from cache (uncached input + output), shown on ten segments.
  const fresh = m && hasCache(m) && m.total > 0 ? (m.total - m.cached) / m.total : null
  const filled = fresh == null ? 0 : fresh > 0 ? Math.max(1, Math.round(fresh * 10)) : 0
  const [number, unit] = m ? [compact(m.total), 'tok'] : ['—', '']

  return (
    <Panel>
      <Block className={s.sessionBlock}>
        <div className={s.reading}>
          <div style={{ minWidth: 0 }}>
            <div className={s.head}>
              <span className={s.kicker}>{t('overview.session')}</span>
              <span className={s.meta}>{m ? t('overview.roundStep', { rounds: m.rounds, steps: m.steps }) : t('live.noSession')}</span>
            </div>
            <div className={s.big}>
              <span className={s.bigNumber}>{number}</span><span className={s.bigUnit}>{unit}</span>
            </div>
            <div className={s.note}>{m ? t('overview.totalNote', { exact: exact(m.total) }) : t('live.sampleHint')}</div>
          </div>
          <div className={s.side}>
            <div className={s.sideItem}>
              <span className={s.sideValue}>{m?.speed != null ? '≈' + Math.round(m.speed * 10) / 10 + ' tok/s' : '—'}</span>
              <span className={s.caption}>{t('stats.speed')}</span>
            </div>
            <div className={s.sideItem}>
              <span className={s.sideValue}>{hitLabel(m)}</span>
              <span className={s.caption}>{t('stats.cache')}</span>
            </div>
          </div>
        </div>
        <div className={s.ruler}>
          <Ruler share={quotaOn ? rulerUsed : null} label={t('overview.quota')} />
          <div className={s.rulerLabels}>
            {quotaOn ? (
              <>
                <span>{settings.ShowFiveHour ? t('overview.fiveUsed', { value: fiveLeft == null ? '—' : Math.round((100 - fiveLeft) * 10) / 10 + '%' }) : ''}</span>
                <span>{settings.ShowWeek ? t('overview.weekLeft', { value: quota?.week?.remaining ?? '—' }) : ''}</span>
              </>
            ) : (
              <>
                <span>{t('overview.quotaOff')}</span>
                <Btn kind="link" onClick={() => go('quota')} style={{ fontSize: 11, color: v('accent') }}>{t('overview.enableQuota')}</Btn>
              </>
            )}
          </div>
        </div>
      </Block>
      <Rule />
      <Block className={s.positionBlock}>
        <div className={s.sectionHead}>
          <span className={s.title}>{t('overview.position')}</span>
          <Btn kind="link" onClick={() => go('placement')}>{t('overview.openPlacement')}<ArrowRight16Regular fontSize={13} /></Btn>
        </div>
        <ComposerPreview settings={settings} usage={usage} quota={quota} scheme={scheme} />
      </Block>
      <Rule />
      <div className={s.ledger}>
        <div className={s.column}>
          <span className={s.colTitle}>{t('stats.total')}</span>
          <span className={s.colSub}>{t('overview.totalSub')}</span>
          <div className={s.colValue}><span className={s.colNumber}>{m ? compact(m.total) : '—'}</span><span className={s.colUnit}>tok</span></div>
          <SegmentBar filled={filled} label={t('overview.freshLegend')} />
          <span className={s.colFoot}>{fresh == null ? '—' : t('overview.freshNote', { value: exact(m.total - m.cached) })}</span>
        </div>
        <div className={s.column}>
          <span className={s.colTitle}>{t('stats.cache')}</span>
          <span className={s.colSub} />
          <div className={s.colValue}><span className={s.colNumber}>{hitLabel(m)}</span></div>
          <Progress value={hitRatio(m)} label={t('stats.cache')} />
          <span className={s.colFoot}>{hasCache(m) ? compact(m.cached) + ' / ' + compact(m.input) : '—'}</span>
        </div>
        <div className={s.column}>
          <span className={s.colTitle}>{t('stats.speed')}</span>
          <span className={s.colSub} />
          <div className={s.colValue}>
            <span className={s.colNumber}>{m?.speed != null ? '≈' + Math.round(m.speed * 10) / 10 : '—'}</span><span className={s.colUnit}>tok/s</span>
          </div>
          <span className={s.colFoot}>{t('stats.speedHint')}</span>
        </div>
      </div>
    </Panel>
  )
}

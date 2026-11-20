import { useEffect, useState } from 'react'
import { makeStyles, Spinner, Switch } from '@fluentui/react-components'
import { ArrowSync20Regular } from '@fluentui/react-icons'
import { useTranslation } from 'react-i18next'
import { Block, Btn, Panel, Rule, Ruler } from '../components/kit.jsx'
import { NUMERIC, v } from '../theme.js'
import { percent } from '../format.js'

const useStyles = makeStyles({
  head: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '16px' },
  title: { fontSize: '15px', fontWeight: 600, color: v('ink') },
  sub: { fontSize: '11px', color: v('muted'), marginTop: '2px' },
  reading: { display: 'flex', alignItems: 'baseline', gap: '8px', margin: '10px 0 12px', fontFamily: NUMERIC, color: v('ink'), whiteSpace: 'nowrap' },
  number: { fontSize: '36px', lineHeight: '42px', fontWeight: 600, letterSpacing: '-0.03em', fontVariantNumeric: 'tabular-nums' },
  unit: { fontSize: '13px', color: v('muted') },
  labels: { display: 'flex', justifyContent: 'space-between', gap: '12px', marginTop: '6px', fontSize: '11px', color: v('muted'), fontFamily: NUMERIC, fontVariantNumeric: 'tabular-nums' },
  off: { opacity: 0.45 },
  status: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' },
  statusText: { display: 'flex', flexDirection: 'column', gap: '2px', fontSize: '11px', color: v('muted'), fontFamily: NUMERIC, fontVariantNumeric: 'tabular-nums' },
  strong: { color: v('ink'), fontSize: '13px', fontWeight: 600 },
})

// Host statuses look like "更新于 20:29:24 · Codex"; anything else is shown as sent.
export function localizeStatus(text, t) {
  if (!text) return ''
  const match = /^更新于 (\d{1,2}:\d{2}(?::\d{2})?) · (.+)$/.exec(text)
  if (match) return t('quota.updated', { time: match[1], source: match[2] === '会话日志' ? t('quota.fromLog') : match[2] })
  if (text === '等待首次刷新') return t('quota.waiting')
  return text
}

function Window({ name, field, data, settings, saved, change }) {
  const { t } = useTranslation()
  const s = useStyles()
  const on = settings[field], live = on && saved[field]
  const left = live ? percent(data?.remaining) : null
  return (
    <Block>
      <div className={s.head}>
        <div>
          <div className={s.title}>{t('quota.' + name)}</div>
          <div className={s.sub}>{on ? (live ? t('quota.shown') : t('quota.pending')) : t('quota.off')}</div>
        </div>
        <Switch checked={on} onChange={(_, d) => change(field, d.checked)} aria-label={t('quota.' + name)} />
      </div>
      <div className={on ? undefined : s.off}>
        <div className={s.reading}>
          <span className={s.number}>{left == null ? '—' : data.remaining}</span>
          <span className={s.unit}>{t('quota.remainingLabel')}</span>
        </div>
        <Ruler share={left == null ? null : (100 - left) / 100} label={t('quota.' + name)} />
        <div className={s.labels}>
          <span>{left == null ? t('quota.noData') : t('quota.usedShare', { value: Math.round((100 - left) * 10) / 10 + '%' })}</span>
          <span>{live && data ? t('quota.resets', { time: data.resets }) : ''}</span>
        </div>
      </div>
    </Block>
  )
}

export function Quota({ settings, saved, change, quota, quotaStatus, refresh }) {
  const { t } = useTranslation()
  const s = useStyles()
  const [refreshing, setRefreshing] = useState(false)
  useEffect(() => { setRefreshing(false) }, [quotaStatus, quota])
  const active = saved.ShowFiveHour || saved.ShowWeek
  return (
    <Panel>
      <Window name="fiveHour" field="ShowFiveHour" data={quota?.fiveHour} {...{ settings, saved, change }} />
      <Rule />
      <Window name="week" field="ShowWeek" data={quota?.week} {...{ settings, saved, change }} />
      <Rule />
      <Block>
        <div className={s.status}>
          <div className={s.statusText}>
            <span className={s.strong}>{active ? localizeStatus(quotaStatus, t) || t('quota.waiting') : t('quota.pending')}</span>
            <span>{t('quota.sourceValue')}</span>
          </div>
          <Btn size="small" disabled={!active || refreshing}
            onClick={() => { setRefreshing(true); refresh(); setTimeout(() => setRefreshing(false), 15000) }}>
            {refreshing ? <Spinner size="tiny" /> : <ArrowSync20Regular />}{refreshing ? t('quota.refreshing') : t('quota.refresh')}
          </Btn>
        </div>
      </Block>
    </Panel>
  )
}

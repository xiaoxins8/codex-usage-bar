import { Dropdown, makeStyles, Option, Switch } from '@fluentui/react-components'
import { useTranslation } from 'react-i18next'
import { Block, Panel, Rule } from '../components/kit.jsx'
import { LANGUAGES } from '../i18n.js'
import { v } from '../theme.js'

const useStyles = makeStyles({
  row: { display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) auto', alignItems: 'center', columnGap: '20px' },
  label: { fontSize: '14px', fontWeight: 600, color: v('ink') },
  desc: { fontSize: '11px', color: v('muted'), marginTop: '3px', lineHeight: '17px' },
  disabled: { opacity: 0.4 },
  mono: { fontFamily: "'Cascadia Mono', Consolas, monospace", fontSize: '10px', color: v('muted') },
  dropdown: { minWidth: '140px' },
})

function Row({ label, desc, control, disabled }) {
  const s = useStyles()
  return (
    <Block>
      <div className={s.row}>
        <div className={disabled ? s.disabled : undefined}>
          <div className={s.label}>{label}</div>
          {desc && <div className={s.desc}>{desc}</div>}
        </div>
        {control}
      </div>
    </Block>
  )
}

export function General({ settings, change, startupAvailable, version }) {
  const { t } = useTranslation()
  const s = useStyles()
  const languageLabel = code => (code ? t('language.' + code) : t('language.system'))
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <Panel>
        <Row label={t('general.startup')} disabled={!startupAvailable}
          desc={startupAvailable ? t('general.startupDesc') : t('general.startupUnavailable')}
          control={<Switch checked={settings.Startup} disabled={!startupAvailable} aria-label={t('general.startup')}
            onChange={(_, d) => change('Startup', d.checked)} />} />
        <Rule />
        <Row label={t('general.language')} desc={t('general.languageDesc')}
          control={<Dropdown className={s.dropdown} aria-label={t('general.language')} value={languageLabel(settings.Language)}
            selectedOptions={[settings.Language]} onOptionSelect={(_, d) => change('Language', d.optionValue)}>
            {LANGUAGES.map(code => <Option key={code || 'system'} value={code}>{languageLabel(code)}</Option>)}
          </Dropdown>} />
      </Panel>
      <Panel>
        <Row label={t('general.privacy')} desc={t('general.privacyText')} />
        <Rule />
        <Row label={t('general.about')} desc={<>{t('general.aboutText')} · {t('app.version', { version })}<br /><span className={s.mono}>{t('general.logPath')}</span></>} />
      </Panel>
    </div>
  )
}

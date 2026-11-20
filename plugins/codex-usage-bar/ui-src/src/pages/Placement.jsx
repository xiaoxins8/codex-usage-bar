import { makeStyles } from '@fluentui/react-components'
import { ArrowReset20Regular, TextAlignCenter20Regular, TextAlignLeft20Regular, TextAlignRight20Regular } from '@fluentui/react-icons'
import { useTranslation } from 'react-i18next'
import { Block, Btn, Choice, OffsetField, Panel, Rule } from '../components/kit.jsx'
import { ComposerPreview } from '../components/ComposerPreview.jsx'
import { v } from '../theme.js'
import { OFFSET_X, OFFSET_Y } from '../geometry.js'

const useStyles = makeStyles({
  sectionHead: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', marginBottom: '10px', flexWrap: 'wrap' },
  title: { fontSize: '15px', fontWeight: 600, color: v('ink') },
  hint: { fontSize: '11px', color: v('muted') },
  row: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap', marginBottom: '18px' },
  field: { display: 'flex', flexDirection: 'column', gap: '7px' },
  label: { fontSize: '14px', fontWeight: 600, color: v('ink') },
  offsets: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '16px 28px' },
  note: { fontSize: '11px', color: v('muted'), marginTop: '16px', lineHeight: '17px' },
})

export function Placement({ settings, change, usage, quota, scheme }) {
  const { t } = useTranslation()
  const s = useStyles()
  const atOrigin = settings.Alignment === 0 && Number(settings.OffsetX) === 0 && Number(settings.OffsetY) === 0
  return (
    <Panel>
      <Block>
        <div className={s.sectionHead}>
          <span className={s.title}>{t('overview.position')}</span>
          <span className={s.hint}>{t('placement.dragHint')}</span>
        </div>
        <ComposerPreview settings={settings} usage={usage} quota={quota} scheme={scheme} guides change={change} />
      </Block>
      <Rule />
      <Block>
        <div className={s.row}>
          <div className={s.field}>
            <span className={s.label}>{t('placement.alignment')}</span>
            <Choice label={t('placement.alignment')} value={settings.Alignment} onChange={value => change('Alignment', value)} options={[
              { value: 2, label: t('placement.left'), icon: <TextAlignLeft20Regular /> },
              { value: 1, label: t('placement.center'), icon: <TextAlignCenter20Regular /> },
              { value: 0, label: t('placement.right'), icon: <TextAlignRight20Regular /> },
            ]} />
            <span className={s.hint}>{t('placement.alignmentHint')}</span>
          </div>
          <Btn size="small" disabled={atOrigin} onClick={() => { change('Alignment', 0); change('OffsetX', 0); change('OffsetY', 0) }}>
            <ArrowReset20Regular />{t('placement.reset')}
          </Btn>
        </div>
        <div className={s.offsets}>
          <OffsetField label={t('placement.offsetX')} hint={t('placement.offsetXHint')} min={OFFSET_X.min} max={OFFSET_X.max}
            increase={t('placement.increase')} decrease={t('placement.decrease')} value={settings.OffsetX} onChange={value => change('OffsetX', value)} />
          <OffsetField label={t('placement.offsetY')} hint={t('placement.offsetYHint')} min={OFFSET_Y.min} max={OFFSET_Y.max}
            increase={t('placement.increase')} decrease={t('placement.decrease')} value={settings.OffsetY} onChange={value => change('OffsetY', value)} />
        </div>
        <div className={s.note}>{t('placement.note')} {t('preview.density')}</div>
      </Block>
    </Panel>
  )
}

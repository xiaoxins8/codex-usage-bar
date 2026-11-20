import { useEffect, useState } from 'react'
import { createDarkTheme, createLightTheme } from '@fluentui/react-components'

// v3 design (用量栏控制中心-设计说明.md): warm paper, ink, one electric blue accent, green only for "applied / listening".
export const PALETTE = {
  light: {
    window: '#F8F4EE', main: '#FBF9F7', paper: '#FDFBF9', ink: '#0D0B0B', muted: '#6B6F76', faint: '#A3A6AC',
    hairline: '#E2DED6', accent: '#0D45F0', accentHover: '#0B3BD0', accentSoft: '#E8EDFD', selected: '#ECEDF2',
    green: '#1E8E5A', amber: '#B7700A', ghost: '#F2EFEA', ghostLine: '#E4E0D9', shadow: '0 1px 2px rgba(40,30,20,.04), 0 8px 24px rgba(40,30,20,.05)',
  },
  dark: {
    window: '#1A1917', main: '#1F1E1C', paper: '#252422', ink: '#F3F0EA', muted: '#A39F98', faint: '#6F6B65',
    hairline: '#36342F', accent: '#5C80FF', accentHover: '#7593FF', accentSoft: '#1F2A4D', selected: '#2D2E33',
    green: '#43C08A', amber: '#E0A23A', ghost: '#2A2926', ghostLine: '#3A3833', shadow: '0 1px 2px rgba(0,0,0,.3), 0 8px 24px rgba(0,0,0,.25)',
  },
}

// CSS custom properties consumed by makeStyles as var(--name).
export const cssVars = scheme => Object.fromEntries(Object.entries(PALETTE[scheme]).map(([k, v]) => ['--' + k, v]))
export const v = name => `var(--${name})`

export const FONT = "'Segoe UI Variable Text', 'Segoe UI', 'Microsoft YaHei UI', 'Microsoft JhengHei UI', system-ui, sans-serif"
// Proportional numerals with tabular figures: monospace would spread "94.1K" apart.
export const NUMERIC = "'Inter', 'Segoe UI Variable Display', 'Segoe UI', 'Microsoft YaHei UI', system-ui, sans-serif"

// Electric blue ramp, #0D45F0 at 80.
const brand = {
  10: '#020a26', 20: '#051647', 30: '#072165', 40: '#092c82', 50: '#0a379f', 60: '#0b3fbd', 70: '#0c42d6', 80: '#0d45f0',
  90: '#3561f3', 100: '#567bf5', 110: '#7392f7', 120: '#8ea8f9', 130: '#a8bdfa', 140: '#c1d0fc', 150: '#d9e3fd', 160: '#eef2fe',
}

function withPalette(base, p) {
  return {
    ...base,
    fontFamilyBase: FONT, fontFamilyNumeric: NUMERIC,
    colorNeutralBackground1: p.paper, colorNeutralBackground2: p.main, colorNeutralBackground3: p.window,
    colorNeutralBackground1Hover: p.selected, colorNeutralBackground1Pressed: p.selected,
    colorNeutralForeground1: p.ink, colorNeutralForeground2: p.ink, colorNeutralForeground3: p.muted, colorNeutralForeground4: p.faint,
    colorNeutralStroke1: p.hairline, colorNeutralStroke2: p.hairline, colorNeutralStroke3: p.hairline,
    colorNeutralStrokeAccessible: p.muted,
    colorBrandBackground: p.accent, colorBrandBackgroundHover: p.accentHover, colorBrandBackgroundPressed: p.accentHover,
    colorCompoundBrandBackground: p.accent, colorCompoundBrandBackgroundHover: p.accentHover, colorCompoundBrandStroke: p.accent,
    colorBrandForeground1: p.accent, colorBrandForegroundLink: p.accent, colorBrandStroke1: p.accent,
  }
}

export const lightTheme = withPalette(createLightTheme(brand), PALETTE.light)
export const darkTheme = withPalette(createDarkTheme(brand), PALETTE.dark)

// Capsule palette from src/Capsule.cs (GDI colors), so the preview matches the real overlay.
export const CAPSULE = {
  light: { fill: '#fdfdfe', edge: '#e8e9ed', text: '#262c37', muted: '#5c6572', green: '#108044', blue: '#2d68ac', dot: '#876aee' },
  dark: { fill: '#27282c', edge: '#3f4147', text: '#edeff2', muted: '#aaafb8', green: '#66dc99', blue: '#8dbcff', dot: '#b19eff' },
}

export function useColorScheme() {
  const query = '(prefers-color-scheme: dark)'
  const [dark, setDark] = useState(() => window.matchMedia(query).matches)
  useEffect(() => {
    const media = window.matchMedia(query)
    const update = () => setDark(media.matches)
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [])
  return dark ? 'dark' : 'light'
}

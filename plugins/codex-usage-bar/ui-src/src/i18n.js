import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import zhCN from './locales/zh-CN.json'
import zhTW from './locales/zh-TW.json'
import en from './locales/en.json'

export const LANGUAGES = ['', 'zh-CN', 'zh-TW', 'en']

// "" follows the Windows display language that WebView2 reports.
export function resolveLanguage(preference) {
  if (LANGUAGES.includes(preference) && preference) return preference
  const system = (navigator.languages && navigator.languages[0]) || navigator.language || 'zh-CN'
  if (/^zh[-_](tw|hk|mo|hant)/i.test(system)) return 'zh-TW'
  if (/^zh/i.test(system)) return 'zh-CN'
  return 'en'
}

i18n.use(initReactI18next).init({
  resources: { 'zh-CN': { translation: zhCN }, 'zh-TW': { translation: zhTW }, en: { translation: en } },
  lng: resolveLanguage(''),
  fallbackLng: 'zh-CN',
  interpolation: { escapeValue: false },
  showSupportNotice: false,
})

export function applyLanguage(preference) {
  const code = resolveLanguage(preference)
  if (i18n.language !== code) i18n.changeLanguage(code)
  document.documentElement.lang = code
}

// Notices arrive from the C# host as Simplified Chinese sentences; map the known ones to keys.
const NOTICES = {
  '设置已保存。切回 Codex 查看。': 'notice.saved',
  '无法保存设置。请检查本地目录权限。': 'notice.saveFailed',
  '自启未保存。请把程序移到较短的路径。': 'notice.startupPath',
  '其他设置已保存，Windows 自启更新失败。': 'notice.startupFailed',
}
export const noticeKey = text => NOTICES[text]

const STATUSES = {
  '跟随当前会话': 'live.following',
  '已固定会话': 'live.pinned',
  '识别会话…': 'live.identifying',
  '正在识别当前会话，可从托盘手动固定': 'live.identifying',
  '等待用量': 'live.waiting',
}
export const statusKey = text => STATUSES[text]

export default i18n

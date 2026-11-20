// Bundles the settings panel into ../ui/settings.js (one IIFE, React included) and writes
// ../ui/THIRD_PARTY_NOTICES.txt for every npm package that ends up in the bundle.
// build.ps1 then inlines it into settings.bundle.html, which is embedded in CodexUsageBar.exe.
import * as esbuild from 'esbuild'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.dirname(fileURLToPath(import.meta.url))
const ui = path.resolve(root, '..', 'ui')
const watch = process.argv.includes('--watch')

const options = {
  absWorkingDir: root,
  entryPoints: ['src/main.jsx'],
  outfile: path.join(ui, 'settings.js'),
  bundle: true,
  format: 'iife',
  platform: 'browser',
  // WebView2 is an evergreen Chromium.
  target: ['chrome110'],
  jsx: 'automatic',
  minify: !watch,
  charset: 'utf8',
  legalComments: 'none',
  metafile: true,
  define: { 'process.env.NODE_ENV': watch ? '"development"' : '"production"' },
  logLevel: 'info',
}

function notices(metafile) {
  const packages = new Map()
  for (const input of Object.keys(metafile.inputs)) {
    const match = input.replace(/\\/g, '/').match(/node_modules\/((?:@[^/]+\/)?[^/]+)\//)
    if (match) packages.set(match[1], path.join(root, 'node_modules', match[1]))
  }
  const parts = ['Third-party software bundled in the Codex 用量栏 settings panel (ui/settings.js).', '']
  for (const [name, dir] of [...packages].sort(([a], [b]) => a.localeCompare(b))) {
    const meta = JSON.parse(fs.readFileSync(path.join(dir, 'package.json'), 'utf8'))
    const file = fs.readdirSync(dir).find(f => /^licen[cs]e(\.|$)/i.test(f))
    parts.push('='.repeat(72), `${name}@${meta.version} — ${meta.license ?? 'see package'}`, '='.repeat(72))
    parts.push(file ? fs.readFileSync(path.join(dir, file), 'utf8').trim() : `License: ${meta.license}`, '')
  }
  fs.writeFileSync(path.join(ui, 'THIRD_PARTY_NOTICES.txt'), parts.join('\n'))
  return packages.size
}

if (watch) {
  const context = await esbuild.context(options)
  await context.watch()
} else {
  const result = await esbuild.build(options)
  const code = fs.readFileSync(options.outfile, 'utf8')
  // The bundle is inlined into a <script> element; these sequences would end or corrupt it.
  if (/<\/script/i.test(code) || /<!--/.test(code)) throw new Error('bundle contains </script or <!-- and cannot be inlined')
  const count = notices(result.metafile)
  console.log(`settings.js ${(code.length / 1024).toFixed(0)} KB · ${count} packages in THIRD_PARTY_NOTICES.txt`)
}

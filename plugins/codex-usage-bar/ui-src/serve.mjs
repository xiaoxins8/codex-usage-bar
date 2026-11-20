// Inlines the current build so "npm run watch" changes appear on browser reload.
// Outside WebView2 the page runs in demo mode with sample data.
import http from 'node:http'
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

// Optional first argument: another built page, e.g. a design draft's preview.html.
const here = path.dirname(fileURLToPath(import.meta.url))
const ui = path.resolve(here, '..', 'ui')
const page = process.argv[2] ? path.resolve(process.argv[2]) : null
const port = Number(process.argv[3] || process.env.PORT) || 5179
http.createServer(async (request, response) => {
  if (request.url !== '/' && !request.url.startsWith('/?')) { response.writeHead(404).end(); return }
  try {
    let html
    if (page) html = await readFile(page, 'utf8')
    else {
      const [template, css, script] = await Promise.all(
        ['index.html', 'settings.css', 'settings.js'].map(name => readFile(path.join(ui, name), 'utf8')))
      html = template.replace('/* SETTINGS_CSS */', () => css).replace('/* SETTINGS_APP */', () => script)
    }
    response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }).end(html)
  } catch (error) {
    console.error(error.message)
    response.writeHead(503, { 'Content-Type': 'text/plain; charset=utf-8' }).end('预览文件不可用。请检查路径或先运行 npm run build。')
  }
}).listen(port, '127.0.0.1', () => console.log(`settings preview on http://localhost:${port}`))

param([switch]$UseBundledUi)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$binary = Join-Path $PSScriptRoot 'CodexUsageBar.exe'
$ui = Join-Path $PSScriptRoot 'ui'
$uiSource = Join-Path $PSScriptRoot 'ui-src'
$vendor = Join-Path $ui 'vendor'
$core = Join-Path $vendor 'Microsoft.Web.WebView2.Core.dll'
$web = Join-Path $vendor 'Microsoft.Web.WebView2.WinForms.dll'
$loader = Join-Path $vendor 'WebView2Loader.dll'

# Settings panel: React + Fluent UI + i18next, bundled by esbuild into ui\settings.js.
# The release ships settings.js so the executable can also be rebuilt offline.
$npm = Get-Command npm.cmd -ErrorAction SilentlyContinue
if (-not $UseBundledUi -and $npm -and (Test-Path -LiteralPath (Join-Path $uiSource 'package.json'))) {
    Push-Location $uiSource
    try {
        if (-not (Test-Path -LiteralPath 'node_modules')) {
            & $npm.Source ci --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw "npm ci 失败 ($LASTEXITCODE)。可运行 .\build.ps1 -UseBundledUi 使用发布包内的设置页。" }
        }
        & $npm.Source run --silent build
        if ($LASTEXITCODE -ne 0) { throw "设置页构建失败 ($LASTEXITCODE)" }
    } finally { Pop-Location }
} else {
    Write-Host '使用发布包内的 ui\settings.js。'
}

$template = Join-Path $ui 'index.html'
$style = Join-Path $ui 'settings.css'
$script = Join-Path $ui 'settings.js'
foreach ($inputFile in @($template, $style, $script, $core, $web, $loader)) {
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "缺少构建文件：$inputFile" }
}
$html = [IO.File]::ReadAllText($template)
if (-not $html.Contains('/* SETTINGS_CSS */') -or -not $html.Contains('/* SETTINGS_APP */')) {
    throw 'ui\index.html 缺少设置页的样式或脚本占位符。'
}
$scriptText = [IO.File]::ReadAllText($script)
if ([String]::IsNullOrWhiteSpace($scriptText) -or $scriptText -match '</script|<!--') {
    throw 'ui\settings.js 为空，或包含无法内联的 HTML 标签。'
}
$html = $html.Replace('/* SETTINGS_CSS */', [IO.File]::ReadAllText($style))
$html = $html.Replace('/* SETTINGS_APP */', $scriptText)
$bundled = Join-Path $ui 'settings.bundle.html'
[IO.File]::WriteAllText($bundled, $html, [Text.UTF8Encoding]::new($false))
& "$framework/csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$binary" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$framework/WPF/UIAutomationClient.dll" "/r:$framework/WPF/UIAutomationTypes.dll" "/r:$framework/WPF/WindowsBase.dll" "/r:$core" "/r:$web" "/resource:$bundled,settings.html" "/resource:$core,Microsoft.Web.WebView2.Core.dll" "/resource:$web,Microsoft.Web.WebView2.WinForms.dll" "/resource:$loader,WebView2Loader.dll" @sources
if ($LASTEXITCODE -ne 0) { throw "编译失败 ($LASTEXITCODE)" }

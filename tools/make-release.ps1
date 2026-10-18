<#
  Texture Lens 发布包：白名单只含游戏会读的文件，结构上不含 Source。
  产出：dist\<ModName>-<版本>\ 和 .zip
  用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-release.ps1
  注意：本文件必须带 UTF-8 BOM，否则 Windows PowerShell 5.1 按 ANSI 读中文会语法报错。
#>
param(
    [string]$ModName = '',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
if (-not $ModName) { $ModName = Split-Path -Leaf $modRoot }
if (-not $OutDir) { $OutDir = Join-Path $modRoot 'dist' }

# ---- 版本号三处一致 ----
$aboutPath = Join-Path $modRoot 'About\About.xml'
if (-not (Test-Path -LiteralPath $aboutPath)) { throw "缺少 $aboutPath" }
[xml]$about = Get-Content -LiteralPath $aboutPath -Raw -Encoding UTF8
$verAbout = [string]$about.ModMetaData.modVersion

$modCs = Get-ChildItem -LiteralPath (Join-Path $modRoot 'Source') -Recurse -File -Filter '*Mod.cs' |
         Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-Object -First 1
if (-not $modCs) { throw '在 Source 下没找到 *Mod.cs' }
$verCs = [regex]::Match((Get-Content -LiteralPath $modCs.FullName -Raw -Encoding UTF8), 'const\s+string\s+Version\s*=\s*"([^"]+)"').Groups[1].Value

$csproj = Get-ChildItem -LiteralPath (Join-Path $modRoot 'Source') -Recurse -File -Filter '*.csproj' |
          Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-Object -First 1
$verProj = if ($csproj) { [regex]::Match((Get-Content -LiteralPath $csproj.FullName -Raw -Encoding UTF8), '<Version>([^<]+)</Version>').Groups[1].Value } else { '' }

Write-Host "版本：About=$verAbout  Mod.cs=$verCs  csproj=$verProj"
if (-not $verCs) { throw '在 Mod.cs 里没找到版本号' }
if ($verProj -and $verAbout -ne $verProj) { throw 'About.xml 与 csproj 版本号不一致' }
if ($verAbout -ne $verCs) { throw 'About.xml 与 Mod.cs 版本号不一致' }

# ---- 白名单复制 ----
$include = @('About', 'Assemblies', 'Defs', 'Languages', 'Textures', 'Sounds', 'Patches', 'LoadFolders.xml')
$dest = Join-Path $OutDir ("{0}-{1}" -f $ModName, $verAbout)
if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
New-Item -ItemType Directory -Path $dest -Force | Out-Null
foreach ($item in $include) {
    $src = Join-Path $modRoot $item
    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force }
}
Get-ChildItem -LiteralPath $dest -Recurse -Directory -Force | Where-Object { $_.Name -in @('bin', 'obj') } | Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $dest -Recurse -File -Force | Where-Object { $_.Extension -in @('.pdb', '.user') } | Remove-Item -Force

$zip = $dest + '.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $dest -DestinationPath $zip -CompressionLevel Optimal

$files = @(Get-ChildItem -LiteralPath $dest -Recurse -File -Force)
Write-Host ("包内文件：{0} 个 / {1:N2} MB" -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))
Write-Host ("发布包：{0} ({1:N0} B)" -f $zip, (Get-Item -LiteralPath $zip).Length)
Write-Host ("检查 Source 是否存在：{0}（应为 False）" -f (Test-Path -LiteralPath (Join-Path $dest 'Source')))

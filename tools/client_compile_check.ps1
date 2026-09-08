<#
.SYNOPSIS
  离线 Roslyn 编译体检:~2s 内验证 Assets/Scripts 运行时程序集能否编译(退出码 0 = 绿)。

.DESCRIPTION
  Unity 编辑器常开着这个工程(Temp/UnityLockfile),batchmode 进不去;这个脚本用 dotnet SDK 自带的 csc
  直接编 Assets/Scripts/**/*.cs。引用/宏每次从 MmorpgClient.csproj 现抽(HintPath / DefineConstants /
  LangVersion / NoWarn),csproj 里的 Unity 安装路径若已不存在(编辑器升级后 Unity 要等下次 regenerate 才会
  重写 csproj),自动映射到 C:\Program Files\Unity\Hub\Editor 下已安装的最新版本。没有 csproj 时退回到
  tools/compile_check/runtime_refs.rsp(静态快照)。产物写到 %TEMP%,不污染工作区。
  用途:出包前的绿灯检查;并行会话把工程改成半成品时,轮询等它恢复再出包(别改对方的文件)。
  输出的 "skipped missing references" 若非空,说明引用 DLL 缺失(Library 正在重建 / 插件被拆),红灯可能只是引用问题。

.EXAMPLE
  pwsh -File tools/client_compile_check.ps1
  pwsh -File tools/client_compile_check.ps1 -ShowErrors 40
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$Csproj = "",
    [string]$FallbackRsp = (Join-Path $PSScriptRoot "compile_check/runtime_refs.rsp"),
    [string]$CscDll = "",
    [string]$UnityRoot = "",
    [int]$ShowErrors = 8
)
$ErrorActionPreference = 'Continue'

if (-not $CscDll) {
    $sdk = Get-ChildItem "C:\Program Files\dotnet\sdk" -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "Roslyn\bincore\csc.dll") } |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $sdk) { Write-Host "csc.dll not found under C:\Program Files\dotnet\sdk (install a .NET SDK)"; exit 2 }
    $CscDll = Join-Path $sdk.FullName "Roslyn\bincore\csc.dll"
}
if (-not $UnityRoot) {
    $hub = Get-ChildItem "C:\Program Files\Unity\Hub\Editor" -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "Editor\Unity.exe") } |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($hub) { $UnityRoot = $hub.FullName }
}
if (-not $Csproj) { $Csproj = Join-Path $ProjectPath "MmorpgClient.csproj" }

Set-Location $ProjectPath

# ── 引用与宏 ──────────────────────────────────────
$opts = @()
$refs = @()
if (Test-Path $Csproj) {
    [xml]$xml = Get-Content $Csproj -Raw
    $ns = @{ m = $xml.Project.NamespaceURI }
    $pg = $xml.Project.PropertyGroup | Select-Object -First 1
    $lang = ($xml.Project.PropertyGroup | ForEach-Object { $_.LangVersion } | Where-Object { $_ } | Select-Object -First 1)
    $nowarn = ($xml.Project.PropertyGroup | ForEach-Object { $_.NoWarn } | Where-Object { $_ } | Select-Object -First 1)
    $defines = ($xml.Project.PropertyGroup | ForEach-Object { $_.DefineConstants } | Where-Object { $_ } | Select-Object -First 1)
    $unsafe = ($xml.Project.PropertyGroup | ForEach-Object { $_.AllowUnsafeBlocks } | Where-Object { $_ } | Select-Object -First 1)
    $opts += "-nologo"; $opts += "-target:library"
    if ($lang) { $opts += "-langversion:$lang" } else { $opts += "-langversion:9.0" }
    $opts += "-nowarn:0169,0649" + $(if ($nowarn) { "," + ($nowarn -replace ';', ',') } else { "" })
    if ($unsafe -and $unsafe.ToString().ToLower() -eq 'true') { $opts += "-unsafe" }
    if ($defines) { foreach ($d in ($defines -split ';')) { if ($d.Trim()) { $opts += "-define:" + $d.Trim() } } }
    $refs = @($xml.Project.ItemGroup | ForEach-Object { $_.Reference } | Where-Object { $_ -and $_.HintPath } | ForEach-Object { $_.HintPath })
    $source = "csproj ($(Split-Path $Csproj -Leaf), $($refs.Count) refs)"
} else {
    $lines = Get-Content $FallbackRsp | Where-Object { $_ -notmatch '^"' -and $_ -notmatch '^-out:' }
    $opts = @($lines | Where-Object { $_ -notmatch '^-r:' })
    $refs = @($lines | Where-Object { $_ -match '^-r:"?(.+?)"?$' } | ForEach-Object { $Matches[1] })
    $source = "fallback rsp ($($refs.Count) refs)"
}

# csproj 的 Unity 路径可能停在旧版本:把 ...\Hub\Editor\<旧版本>\ 映射到已安装版本。
$missing = @(); $remapped = 0
$refArgs = foreach ($ref in $refs) {
    $full = if ([IO.Path]::IsPathRooted($ref)) { $ref } else { Join-Path $ProjectPath $ref }
    if (-not (Test-Path $full) -and $UnityRoot -and $full -match '^(.*\\Unity\\Hub\\Editor\\)[^\\]+\\(.*)$') {
        $cand = Join-Path $UnityRoot $Matches[2]
        if (Test-Path $cand) { $full = $cand; $remapped++ }
    }
    if (Test-Path $full) { '-r:"' + $full + '"' } else { $missing += $ref }
}
if ($remapped -gt 0) { "remapped $remapped Unity references to $UnityRoot (csproj still points at an uninstalled editor; regenerate project files in Unity to silence this)" }
if ($missing.Count -gt 0) { "skipped missing references ($($missing.Count)): " + (($missing | Select-Object -First 6) -join '; ') + $(if ($missing.Count -gt 6) { " ..." } else { "" }) }

# ── 源文件 ────────────────────────────────────────
$srcs = Get-ChildItem Assets\Scripts -Recurse -Filter *.cs | ForEach-Object {
    '"' + $_.FullName.Substring($ProjectPath.Length).TrimStart('\', '/').Replace('\', '/') + '"'
}
$tmp = Join-Path ([IO.Path]::GetTempPath()) "mmorpg_compile_check"
New-Item -ItemType Directory -Force $tmp | Out-Null
$rspPoll = Join-Path $tmp "poll.rsp"
Set-Content $rspPoll -Value ($opts + $refArgs + "-out:`"$tmp\check.dll`"" + $srcs) -Encoding utf8

$out = & dotnet exec $CscDll "@$rspPoll" 2>&1
$code = $LASTEXITCODE
$errs = @($out | Where-Object { $_ -match ': error ' })
"$(Get-Date -Format T) exit=$code files=$($srcs.Count) errors=$($errs.Count) refs=$source unity=$UnityRoot csc=$CscDll"
$errs | Select-Object -First $ShowErrors | ForEach-Object { "  " + $_.ToString().Substring(0, [Math]::Min(200, $_.ToString().Length)) }
if ($errs.Count -gt 0) {
    "files with errors: " + (($errs | ForEach-Object { ($_ -split '\(')[0].Trim() } | Sort-Object -Unique) -join ', ')
}
exit $code

<#
.SYNOPSIS
  离线 Roslyn 编译体检:~2s 内验证 Assets/Scripts 运行时程序集能否编译(退出码 0 = 绿)。

.DESCRIPTION
  Unity 编辑器常开着这个工程(Temp/UnityLockfile),batchmode 进不去;这个脚本用 dotnet SDK 自带的 csc
  直接编 Assets/Scripts/**/*.cs。引用/宏每次从 MmorpgClient.csproj 现抽(HintPath / DefineConstants /
  LangVersion / NoWarn),csproj 里的 Unity 安装路径若已不存在(编辑器升级后 Unity 要等下次 regenerate 才会
  重写 csproj),自动映射到 C:\Program Files\Unity\Hub\Editor 下已安装的最新版本。没有 csproj 时退回到
  tools/compile_check/runtime_refs.rsp(静态快照)。每次在独立临时目录编译，避免并行任务覆盖参数和产物。
  默认退出时清理本次临时目录；-KeepArtifacts 保留，-ArtifactDir 指定证据根目录并自动保留。
  证据包含 poll.rsp、compiler.log、result.json，以及编译成功时的 check.dll。
  用途:出包前的绿灯检查;并行会话把工程改成半成品时,轮询等它恢复再出包(别改对方的文件)。
  输出的 "skipped missing references" 若非空,说明引用 DLL 缺失(Library 正在重建 / 插件被拆),红灯可能只是引用问题。

.EXAMPLE
  pwsh -File tools/client_compile_check.ps1
  pwsh -File tools/client_compile_check.ps1 -ShowErrors 40
  pwsh -File tools/client_compile_check.ps1 -ArtifactDir '.codex-artifacts/compile checks'
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$Csproj = "",
    [string]$FallbackRsp = (Join-Path $PSScriptRoot "compile_check/runtime_refs.rsp"),
    [string]$CscDll = "",
    [string]$UnityRoot = "",
    [int]$ShowErrors = 8,
    [string]$ArtifactDir = "",
    [switch]$KeepArtifacts
)
$ErrorActionPreference = 'Continue'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath -ErrorAction Stop).Path
$retainArtifacts = $KeepArtifacts.IsPresent -or -not [string]::IsNullOrWhiteSpace($ArtifactDir)
# Resolve caller-relative evidence paths before changing into the target project.
$artifactRoot = if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    Join-Path ([IO.Path]::GetTempPath()) 'mmorpg_compile_check'
} else {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArtifactDir)
}
$artifactRoot = [IO.Path]::GetFullPath($artifactRoot)

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

# 离线编译会直接收集 .cs，不会经过 Unity 导入器；先拦截会让 Unity 忽略协议源码的元数据错误。
$generatedProtoDirectory = Join-Path $ProjectPath 'Assets/Scripts/Proto/Generated'
if (Test-Path -LiteralPath $generatedProtoDirectory) {
    try {
        & (Join-Path $PSScriptRoot 'sync_proto_script_meta.ps1') -GeneratedDirectory $generatedProtoDirectory -CheckOnly | Out-Null
    }
    catch {
        Write-Host ("Unity protobuf metadata validation failed: " + $_.Exception.Message)
        exit 1
    }
}

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
$runName = "run-$PID-" + [Guid]::NewGuid().ToString('N')
$tmp = [IO.Path]::GetFullPath((Join-Path $artifactRoot $runName))
$rspPoll = Join-Path $tmp 'poll.rsp'
$assemblyPath = Join-Path $tmp 'check.dll'
$startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
$ownsDirectory = $false
$code = 2
try {
    $null = New-Item -ItemType Directory -Path $artifactRoot -Force -ErrorAction Stop
    # No -Force: even an unexpected name collision must not reuse another run.
    $null = New-Item -ItemType Directory -Path $tmp -ErrorAction Stop
    $ownsDirectory = $true
    Set-Content -LiteralPath $rspPoll -Value ($opts + $refArgs + "-out:`"$assemblyPath`"" + $srcs) -Encoding utf8 -ErrorAction Stop

    $out = @(& dotnet exec $CscDll "@$rspPoll" 2>&1)
    $code = $LASTEXITCODE
    $errs = @($out | Where-Object { $_ -match ': error ' })
    Set-Content -LiteralPath (Join-Path $tmp 'compiler.log') -Value ($out | ForEach-Object { $_.ToString() }) -Encoding utf8 -ErrorAction Stop
    [ordered]@{
        schemaVersion = 1; projectPath = $ProjectPath; compiler = $CscDll; unityRoot = $UnityRoot
        sourceCount = @($srcs).Count; referenceSource = $source; missingReferences = @($missing)
        exitCode = $code; errorCount = $errs.Count; responseFile = $rspPoll; assembly = $assemblyPath
        startedUtc = $startedUtc; finishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $tmp 'result.json') -Encoding utf8 -ErrorAction Stop
    "$(Get-Date -Format T) exit=$code files=$($srcs.Count) errors=$($errs.Count) refs=$source unity=$UnityRoot csc=$CscDll"
    $errs | Select-Object -First $ShowErrors | ForEach-Object { "  " + $_.ToString().Substring(0, [Math]::Min(200, $_.ToString().Length)) }
    if ($errs.Count -gt 0) {
        "files with errors: " + (($errs | ForEach-Object { ($_ -split '\(')[0].Trim() } | Sort-Object -Unique) -join ', ')
    }
} catch {
    $code = 2
    Write-Error -Message ("Compile check infrastructure failed: " + $_.Exception.Message) -ErrorAction Continue
} finally {
    if ($ownsDirectory -and -not $retainArtifacts) {
        # Only remove this invocation's exact child directory, never the shared root.
        $resolvedRun = [IO.Path]::GetFullPath($tmp)
        $expectedRun = [IO.Path]::GetFullPath((Join-Path $artifactRoot $runName))
        if ($resolvedRun -ne $expectedRun -or (Split-Path $resolvedRun -Leaf) -ne $runName) {
            $retainArtifacts = $true
            Write-Warning 'Temporary directory identity changed; artifacts were retained.'
        } else {
            try { Remove-Item -LiteralPath $resolvedRun -Recurse -Force -ErrorAction Stop }
            catch { $retainArtifacts = $true; Write-Warning ("Could not clean this run's artifacts: " + $_.Exception.Message) }
        }
    }
    if ($ownsDirectory) { "compile-check artifacts=$tmp retained=$($retainArtifacts.ToString().ToLowerInvariant())" }
}
exit $code

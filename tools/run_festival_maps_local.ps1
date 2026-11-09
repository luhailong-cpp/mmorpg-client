#requires -Version 7.0
<#
.SYNOPSIS
  在 Unity Editor 中启动 9 图节庆地图本机联调，正式传送门禁保持关闭。
.EXAMPLE
  pwsh -File tools/run_festival_maps_local.ps1 -Editor
.EXAMPLE
  pwsh -File tools/run_festival_maps_local.ps1 -Editor -LocalServerEnvFile ../xuanming-server-mmo-cpp/build/festival-maps-20261109/stack/.env
#>
[CmdletBinding()]
param(
    [string]$Gateway = 'http://127.0.0.1:18185',
    [string]$UnityExe,
    [string]$LocalServerEnvFile,
    [string]$Account = 'dev_festival_manual',
    [switch]$AutoVerify,
    [string]$VerificationOut,
    [switch]$Editor = $true,
    [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'
$clientRoot = Split-Path $PSScriptRoot -Parent
$gatewayUri = $null
if (-not [Uri]::TryCreate($Gateway, [UriKind]::Absolute, [ref]$gatewayUri) -or
    $gatewayUri.Scheme -notin @('http', 'https') -or -not $gatewayUri.IsLoopback -or $gatewayUri.UserInfo) {
    throw '本机联调仅接受不含凭据的 HTTP(S) 回环网关地址。'
}
if (-not $Editor) { throw '本轮入口使用 Unity Editor；不进行播放器打包。' }
$versionText = Get-Content -LiteralPath (Join-Path $clientRoot 'ProjectSettings/ProjectVersion.txt') -Raw
if ($versionText -notmatch '(?m)^m_EditorVersion:\s*(\S+)') { throw '无法读取 Unity 版本。' }
$unityVersion = $Matches[1]
if (-not $UnityExe) {
    $candidates = @(
        (Join-Path $env:ProgramFiles "Unity/Hub/Editor/$unityVersion/Editor/Unity.exe")
        (Join-Path (Split-Path $clientRoot -Parent) "tools/Unity/$unityVersion/Editor/Unity.exe")
    )
    $UnityExe = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $UnityExe) { throw "找不到 Unity $unityVersion，请传入 -UnityExe。" }
$executable = (Resolve-Path -LiteralPath $UnityExe).Path
$projectPattern = '-projectPath\s+"?' + [regex]::Escape($clientRoot.Replace('\', '/')) + '(?:"|\s|$)'
$opened = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object {
    $_.CommandLine -and $_.CommandLine.Replace('\', '/') -match $projectPattern
}
if ($opened) { throw '主工程已在 Unity 中打开。请先关闭该主工程再启动；其他副本不会被关闭。' }
Write-Host "本机联调编辑器：$executable；正式主场景进入 Play。"
if ($NoLaunch) { return }
$logDirectory = Join-Path $clientRoot 'Logs'
$null = New-Item -ItemType Directory -Path $logDirectory -Force
$playerLog = Join-Path $logDirectory ("festival-local-player-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$info = [Diagnostics.ProcessStartInfo]::new($executable)
$info.UseShellExecute = $false
$info.WorkingDirectory = $clientRoot
if ($Editor) {
    foreach ($argument in @('-projectPath', $clientRoot, '-executeMethod', 'MmorpgClient.Editor.FestivalMapLocalTestEntry.OpenBootstrap', '-job-worker-count', '2')) {
        $info.ArgumentList.Add($argument)
    }
}
if ($LocalServerEnvFile) {
    if ($Account -notmatch '^dev_festival_[a-zA-Z0-9_-]{1,40}$') { throw '本机自动登录账号须使用 dev_festival_ 前缀。' }
    $secretLine = Get-Content -LiteralPath $LocalServerEnvFile | Where-Object { $_ -match '^LOGIN_DEV_PASSWORD_SHARED_SECRET=' } | Select-Object -First 1
    if (-not $secretLine) { throw '测试服务环境文件缺少 LOGIN_DEV_PASSWORD_SHARED_SECRET。' }
    $testPassword = $secretLine.Substring('LOGIN_DEV_PASSWORD_SHARED_SECRET='.Length).Trim().Trim('"', "'")
    if ([string]::IsNullOrWhiteSpace($testPassword) -or $testPassword -match '["\r\n]') { throw '测试服务口令为空或包含不支持的字符。' }
    $info.Environment['MMORPG_AUTOPILOT_ARGS'] = '-zone 1 -gateway "{0}" -account "{1}" -password "{2}"' -f $Gateway, $Account, $testPassword
    $testPassword = $null
    $secretLine = $null
} else {
    if ($AutoVerify) { throw '-AutoVerify 需要 -LocalServerEnvFile 注入隔离测试口令。' }
    $info.ArgumentList.Add('-manual-play')
    $info.Environment.Remove('MMORPG_AUTOPILOT_ARGS') | Out-Null
}
foreach ($argument in @('-gateway', $Gateway, '-festivalMapLocalTest', '-logFile', $playerLog)) {
    $info.ArgumentList.Add($argument)
}
if ($AutoVerify) {
    if (-not $VerificationOut) { $VerificationOut = Join-Path $clientRoot ('.codex-artifacts/festival-local-verify-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
    foreach ($argument in @('-festivalMapLocalVerify', '-currentMapVerifyOut', $VerificationOut, '-currentMapVerifyTimeout', '600')) {
        $info.ArgumentList.Add($argument)
    }
    Write-Host "9 图自动联调证据：$VerificationOut"
}
$process = [Diagnostics.Process]::Start($info)
Write-Host "本机联调 PID：$($process.Id)；日志：$playerLog"
Write-Host '手动登录本机测试账号后，在左上联调面板选择地图/外观。收起面板可用 WASD 与点击行走。'
Write-Host '此脚本不启动、重启或部署服务器；未登记或导航未就绪的场景应被服务器拒绝。'
if ($AutoVerify) {
    $process.WaitForExit()
    $result = $process.ExitCode
    $process.Dispose()
    exit $result
}
$process.Dispose()

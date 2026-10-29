param(
    [string]$ProjectPath = (Split-Path $PSScriptRoot -Parent),
    [string]$EvidencePath = '',
    [int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
if (-not $EvidencePath) { $EvidencePath = Join-Path $ProjectPath '.codex-artifacts/activity-ui-20261022' }
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force -Path $EvidencePath | Out-Null
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$isolated = Join-Path $EvidencePath ('isolated-' + $runId)
New-Item -ItemType Directory -Force -Path "$isolated/Assets/Plugins", "$isolated/Assets/Tests", "$isolated/Packages", "$isolated/ProjectSettings" | Out-Null

# 使用全量正式运行源码生成的程序集，UI测试和截图源码保持原样复制；不打开主工程。
$before = @(Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Assets/Scripts') -Recurse -Filter '*.cs' |
    ForEach-Object { @{ path = $_.FullName.Substring($ProjectPath.Length + 1).Replace('\', '/'); hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
$compileTemporary = Join-Path $isolated 'CompileTemporary'
New-Item -ItemType Directory -Force -Path $compileTemporary | Out-Null
$previousTmp = $env:TMP; $previousTemp = $env:TEMP
try {
    # 并行验证会话不得共用系统临时目录中的check.dll。
    $env:TMP = $compileTemporary; $env:TEMP = $compileTemporary
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'client_compile_check.ps1') -ProjectPath $ProjectPath -ShowErrors 30 2>&1 |
        Tee-Object -FilePath (Join-Path $EvidencePath 'runtime-compile.log')
    $compileExit = $LASTEXITCODE
} finally { $env:TMP = $previousTmp; $env:TEMP = $previousTemp }
if ($compileExit -ne 0) { throw '正式运行源码编译失败，未启动原生验证' }
$runtime = Join-Path $compileTemporary 'mmorpg_compile_check/check.dll'
Copy-Item -LiteralPath $runtime -Destination "$isolated/Assets/Plugins/check.dll"
Copy-Item -LiteralPath (Join-Path $ProjectPath 'Assets/Plugins/Google.Protobuf.dll') -Destination "$isolated/Assets/Plugins/Google.Protobuf.dll"
# 运行程序集含全局proto类型，不能自动注入UGUI等包的编译引用。
$pluginMeta = (Get-Content -LiteralPath (Join-Path $ProjectPath 'Assets/Plugins/Google.Protobuf.dll.meta') -Raw)
$pluginMeta = $pluginMeta -replace 'guid: [a-f0-9]+', ('guid: ' + [guid]::NewGuid().ToString('N'))
$pluginMeta = $pluginMeta -replace 'isExplicitlyReferenced: 0', 'isExplicitlyReferenced: 1'
Set-Content -LiteralPath "$isolated/Assets/Plugins/check.dll.meta" -Value $pluginMeta -Encoding utf8
foreach ($source in $before) {
    if ((Get-FileHash -LiteralPath (Join-Path $ProjectPath $source.path) -Algorithm SHA256).Hash -ne $source.hash) {
        throw ('编译期间运行源码变化，不能把快照称为已验证：' + $source.path)
    }
}
$before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidencePath 'runtime-source-hashes.json') -Encoding utf8

$exactSources = @('Assets/Tests/EditMode/Battle/GameplayWindowTests.cs', 'Assets/Tests/EditMode/Battle/ActivityPreferencesTests.cs', 'Assets/Editor/ActivityCenterUiVerification.cs')
$sourceHashes = foreach ($relative in $exactSources) {
    $destination = Join-Path "$isolated/Assets/Tests" (Split-Path $relative -Leaf)
    Copy-Item -LiteralPath (Join-Path $ProjectPath $relative) -Destination $destination
    @{ source = $relative; hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
}
$sourceHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidencePath 'exact-test-source-hashes.json') -Encoding utf8
$escapedEvidence = $EvidencePath.Replace('\', '/').Replace('"', '\"')
Set-Content -LiteralPath "$isolated/Assets/Tests/ActivityCenterCaptureTests.cs" -Encoding utf8 -Value @"
using NUnit.Framework;
public sealed class ActivityCenterCaptureTests {
    [Test] public void RenderBothResolutionsWithProductionControls() {
        ActivityCenterUiVerification.OutputDirectory = "$escapedEvidence";
        ActivityCenterUiVerification.CaptureAll();
    }
}
"@
Set-Content -LiteralPath "$isolated/Assets/Tests/ActivityVerification.Tests.asmdef" -Encoding utf8 -Value '{"name":"ActivityVerification.Tests","references":["UnityEngine.UI","Unity.TextMeshPro"],"includePlatforms":["Editor"],"overrideReferences":true,"precompiledReferences":["check.dll","Google.Protobuf.dll"],"optionalUnityReferences":["TestAssemblies"]}'
foreach ($relative in @('Assets/Resources/Fonts', 'Assets/TextMesh Pro', 'Assets/Resources/UI/Ugui/GameplayFidelity20261016', 'Assets/Resources/UI/Ugui/GameplayV1', 'Assets/Resources/UI/Ugui/EquipmentV1')) {
    $destination = Join-Path $isolated $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $ProjectPath $relative) -Destination $destination -Recurse
}

# 缓存包采用本地file路径；不安装或升级依赖，不要求联网。
$cache = Join-Path $ProjectPath 'Library/PackageCache'
$dependencies = [ordered]@{}
foreach ($name in @('com.unity.ugui', 'com.unity.test-framework', 'com.unity.ext.nunit')) {
    $package = Get-ChildItem -LiteralPath $cache -Directory | Where-Object { $_.Name -eq $name -or $_.Name.StartsWith($name + '@') } | Select-Object -First 1
    if (-not $package) { throw ('缺少已有缓存包：' + $name) }
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\', '/')
}
foreach ($module in @('animation', 'audio', 'imageconversion', 'imgui', 'jsonserialize', 'particlesystem', 'physics', 'physics2d', 'ui', 'uielements', 'unitywebrequest', 'unitywebrequesttexture', 'unitywebrequestassetbundle', 'assetbundle', 'director')) { $dependencies['com.unity.modules.' + $module] = '1.0.0' }
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$isolated/Packages/manifest.json" -Encoding utf8
Copy-Item -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') -Destination "$isolated/ProjectSettings/ProjectVersion.txt"
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
if (-not (Test-Path -LiteralPath $unity)) { throw '当前已安装Unity不存在，不能执行原生验证' }
$arguments = @('-batchmode', '-projectPath', ('"' + $isolated + '"'), '-runTests', '-testPlatform', 'EditMode',
    '-testResults', ('"' + (Join-Path $EvidencePath 'native-ui-results.xml') + '"'), '-logFile', ('"' + (Join-Path $EvidencePath 'native-ui.log') + '"'))
@{ isolatedProject = $isolated; runtimeAssemblySha256 = (Get-FileHash -LiteralPath "$isolated/Assets/Plugins/check.dll" -Algorithm SHA256).Hash;
    productionSourceCount = $before.Count; mainProjectOpened = $false; gameNetworkStarted = $false; liveServerVerified = $false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidencePath 'invocation.json') -Encoding utf8
$process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('活动隔离原生验证 PID=' + $process.Id + '，日志 ' + (Join-Path $EvidencePath 'native-ui.log'))
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) { throw ('原生验证超出预算，保留进程与日志供检查，PID=' + $process.Id) }
}
$process.Refresh()
if ($process.ExitCode -ne 0) { throw ('Unity验证失败，退出码 ' + $process.ExitCode) }
$report = Join-Path $EvidencePath 'native-ui-results.xml'
if (-not (Test-Path -LiteralPath $report)) { throw '未生成原生测试结果，不能报告通过' }
[xml]$xml = Get-Content -LiteralPath $report -Raw
if ($xml.'test-run'.result -ne 'Passed' -or [int]$xml.'test-run'.total -eq 0) { throw ('原生测试未全过：' + $xml.'test-run'.result) }
if (-not (Test-Path -LiteralPath (Join-Path $EvidencePath 'capture-status.json'))) { throw '未生成截图状态，原生视觉验证尚未完成' }
$captureStatus = Get-Content -LiteralPath (Join-Path $EvidencePath 'capture-status.json') -Raw | ConvertFrom-Json
$capturedCount = @(Get-ChildItem -LiteralPath $EvidencePath -Filter '*.png' -File).Count
if ($capturedCount -ne [int]$captureStatus.screenshots) { throw '实际截图数与当前截图入口声明不一致' }
Write-Output ('活动原生验证通过：' + $xml.'test-run'.passed + ' 项测试，' + $capturedCount + '张截图待人工检查')

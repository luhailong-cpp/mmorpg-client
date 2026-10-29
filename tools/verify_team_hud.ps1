param(
    [string]$ProjectPath = (Split-Path $PSScriptRoot -Parent),
    [string]$EvidencePath = '',
    [string]$ReuseProjectPath = '',
    [int]$TimeoutSeconds = 900
)
$ErrorActionPreference = 'Stop'
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
if (-not $EvidencePath) {
    $EvidencePath = Join-Path $ProjectPath ('.codex-artifacts/team-hud-20261022/isolated-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force -Path $EvidencePath | Out-Null
$isolated = Join-Path $EvidencePath 'project'
if (Test-Path -LiteralPath "$EvidencePath/source-before.json") { throw '证据目录已存在，请另选目录，保留原始证据。' }
if ($ReuseProjectPath) {
    $isolated = [IO.Path]::GetFullPath($ReuseProjectPath)
    $allowed = [IO.Path]::GetFullPath((Join-Path $ProjectPath '.codex-artifacts/team-hud-20261022')) + [IO.Path]::DirectorySeparatorChar
    if (-not $isolated.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath "$isolated/Assets/Scripts/MmorpgClient.asmdef")) {
        throw '只能复用本任务已建立的隔离验证工程。'
    }
} elseif (Test-Path -LiteralPath $isolated) { throw '隔离工程已存在，须显式提供ReuseProjectPath。' }
New-Item -ItemType Directory -Force -Path "$isolated/Assets/Plugins", "$isolated/Assets/Tests", "$isolated/Packages", "$isolated/ProjectSettings" | Out-Null

function Copy-ExactTree([string]$original, [string]$destination) {
    $sourceItem = Get-Item -LiteralPath $original
    if (-not $sourceItem.PSIsContainer) { Copy-Item -LiteralPath $original -Destination $destination; return }
    foreach ($item in Get-ChildItem -LiteralPath $original -Recurse -File) {
        $target = Join-Path $destination $item.FullName.Substring($sourceItem.FullName.Length + 1)
        if (Test-Path -LiteralPath $target) {
            $existing = Get-Item -LiteralPath $target
            if ($existing.Length -eq $item.Length -and $existing.LastWriteTimeUtc.Ticks -eq $item.LastWriteTimeUtc.Ticks) { continue }
        }
        if (-not (Test-Path -LiteralPath (Split-Path $target -Parent))) { New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null }
        Copy-Item -LiteralPath $item.FullName -Destination $target
    }
}

# 精确复制正式源码和meta，保留程序集名及ScriptableObject索引的脚本GUID。
$testSources = @(Get-ChildItem -LiteralPath "$ProjectPath/Assets/Tests/EditMode/Tianyong" -File -Filter 'Team*Tests.cs' | ForEach-Object FullName) +
    @("$ProjectPath/Assets/Tests/EditMode/Tianyong/WorldSideHudTests.cs", "$ProjectPath/Assets/Editor/WorldSideHudVerification.cs")
$runtimeFiles = @(Get-ChildItem -LiteralPath "$ProjectPath/Assets/Scripts" -Recurse -File)
$sourceFiles = @($runtimeFiles.FullName) + $testSources + @("$ProjectPath/Assets/Plugins/Google.Protobuf.dll")
$resourcePaths = @(
    'Assets/Resources/Fonts', 'Assets/TextMesh Pro',
    'Assets/Resources/UI/Ugui/TeamV2', 'Assets/Resources/UI/Ugui/TeamExact20261017',
    'Assets/Resources/UI/Ugui/GameplayV1', 'Assets/Resources/UI/Ugui/SocialV1', 'Assets/Resources/UI/Ugui/WorldHud20261022',
    'Assets/Resources/UI/Ugui/Native/status_dot_mask.png',
    'Assets/Resources/UI/Ugui/RefreshV8/sanctuary_background.png',
    'Assets/Resources/UI/Ugui/RefreshV8/round_badge_taiji.png',
    'Assets/Resources/UI/Ugui/RefreshV8/status_dot_orange.png',
    'Assets/Resources/World/Characters/QdaoOriginalRosterV13/00_reference_topright_boy',
    'Assets/Resources/World/Characters/QdaoOriginalRosterV13/01_ice_sword_girl',
    'Assets/Resources/World/Characters/QdaoOriginalRosterV13/03_lotus_healer_girl',
    'Assets/Resources/World/Characters/QdaoOriginalRosterV14/04_mountain_guardian_boy',
    'Assets/Resources/World/Characters/QdaoOriginalRosterV14/06_thunder_caster_boy',
    'Assets/Resources/World/Characters/QdaoOriginalRosterV14/08_alchemy_prodigy_boy',
    'Assets/Resources/World/Characters/QdaoOriginalRosterV14/09_bamboo_archer_girl',
    'Assets/Resources/World/Characters/QdaoOriginalRosterV14/20_star_formation_master_girl'
)
$resourceFiles = foreach ($relative in $resourcePaths) {
    $original = Join-Path $ProjectPath $relative
    if (-not (Test-Path -LiteralPath $original)) { throw ('缺少正式资源：' + $relative) }
    $item = Get-Item -LiteralPath $original
    if ($item.PSIsContainer) { Get-ChildItem -LiteralPath $original -Recurse -File | ForEach-Object FullName }
    else { $original }
    if (Test-Path -LiteralPath ($original + '.meta')) { $original + '.meta' }
}
$allOriginals = @($sourceFiles + $resourceFiles | Sort-Object -Unique)
function Hash-Originals {
    @($allOriginals | ForEach-Object {
        $item = Get-Item -LiteralPath $_
        [pscustomobject]@{ Path=$item.FullName.Substring($ProjectPath.Length + 1).Replace('\','/');
            Sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash;
            WriteUtcTicks=$item.LastWriteTimeUtc.Ticks }
    })
}
$before = Hash-Originals
$before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$EvidencePath/source-before.json" -Encoding utf8
Copy-ExactTree "$ProjectPath/Assets/Scripts" "$isolated/Assets/Scripts"
Copy-Item -LiteralPath "$ProjectPath/Assets/Plugins/Google.Protobuf.dll" -Destination "$isolated/Assets/Plugins/Google.Protobuf.dll"
# 仅隔离工程禁止插件自动注入第三方包；正式asmdef显式引用保持原样。
$pluginMeta = Get-Content -LiteralPath "$ProjectPath/Assets/Plugins/Google.Protobuf.dll.meta" -Raw
Set-Content -LiteralPath "$isolated/Assets/Plugins/Google.Protobuf.dll.meta" -Encoding utf8 -Value ($pluginMeta -replace 'isExplicitlyReferenced: 0', 'isExplicitlyReferenced: 1')
foreach ($source in $testSources) {
    $destination = Join-Path "$isolated/Assets/Tests" (Split-Path $source -Leaf)
    Copy-Item -LiteralPath $source -Destination $destination
    if (Test-Path -LiteralPath ($source + '.meta')) { Copy-Item -LiteralPath ($source + '.meta') -Destination ($destination + '.meta') }
}
Copy-Item -LiteralPath "$ProjectPath/Assets/Tests/EditMode/Tianyong/MmorpgClient.Tests.EditMode.Tianyong.asmdef" -Destination "$isolated/Assets/Tests/MmorpgClient.Tests.EditMode.Tianyong.asmdef"
foreach ($relative in $resourcePaths) {
    $original = Join-Path $ProjectPath $relative
    $destination = Join-Path $isolated $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
    Copy-ExactTree $original $destination
    if (Test-Path -LiteralPath ($original + '.meta')) { Copy-Item -LiteralPath ($original + '.meta') -Destination ($destination + '.meta') }
}
# 核对复制后的内容与修改时间，V14运行索引本来就依赖这些正式文件属性。
foreach ($record in $before) {
    $relative = $record.Path
    if ($relative.StartsWith('Assets/Tests/') -or $relative -eq 'Assets/Editor/WorldSideHudVerification.cs') {
        $destination = Join-Path "$isolated/Assets/Tests" (Split-Path $relative -Leaf)
    } else { $destination = Join-Path $isolated $relative }
    $item = Get-Item -LiteralPath $destination
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $record.Sha256 -or $item.LastWriteTimeUtc.Ticks -ne $record.WriteUtcTicks) {
        throw ('隔离复制未保持原件SHA/时间：' + $relative)
    }
}
$escapedEvidence = $EvidencePath.Replace('\','/').Replace('"','\"')
Set-Content -LiteralPath "$isolated/Assets/Tests/TeamHudCaptureTests.cs" -Encoding utf8 -Value @"
using NUnit.Framework;
public sealed class TeamHudCaptureTests {
    [Test] public void RenderBothResolutionsWithProductionControls() {
        WorldSideHudVerification.OutputDirectory = "$escapedEvidence";
        WorldSideHudVerification.CaptureAll();
    }
}
"@
$cache = Join-Path $ProjectPath 'Library/PackageCache'
$dependencies = [ordered]@{}
foreach ($name in @('com.unity.ugui', 'com.unity.test-framework', 'com.unity.ext.nunit')) {
    $package = Get-ChildItem -LiteralPath $cache -Directory | Where-Object { $_.Name -eq $name -or $_.Name.StartsWith($name + '@') } | Select-Object -First 1
    if (-not $package) { throw ('缺少已有缓存包：' + $name) }
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\','/')
}
foreach ($module in @('animation','audio','imageconversion','imgui','jsonserialize','particlesystem','physics','physics2d','ui','uielements','unitywebrequest','unitywebrequesttexture','unitywebrequestassetbundle','assetbundle','director','screencapture')) {
    $dependencies['com.unity.modules.' + $module] = '1.0.0'
}
@{dependencies=$dependencies} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$isolated/Packages/manifest.json" -Encoding utf8
Copy-Item -LiteralPath "$ProjectPath/ProjectSettings/ProjectVersion.txt" -Destination "$isolated/ProjectSettings/ProjectVersion.txt"
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
if (-not (Test-Path -LiteralPath $unity)) { throw '已安装Unity不可用。' }
$free = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024)
if ($free -lt 3072) { throw ('空闲内存不足3GiB，未启动Unity：' + $free + 'MiB') }
$arguments = @('-batchmode','-projectPath',('"'+$isolated+'"'),'-runTests','-testPlatform','EditMode',
    '-testResults',('"'+$EvidencePath+'/team-tests.xml"'),'-logFile',('"'+$EvidencePath+'/unity.log"'))
[pscustomobject]@{isolatedProject=$isolated; runtimeSourceCount=@($runtimeFiles | Where-Object Extension -eq '.cs').Count;
    resourceFileCount=@($resourceFiles).Count; availableMiB=$free; mainProjectOpened=$false; gameNetworkStarted=$false;
    resourceContentOrIndexModified=$false; pluginImporterAdjustment='仅隔离Google.Protobuf设为显式引用';
    startedAt=[DateTimeOffset]::Now.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath "$EvidencePath/invocation.json" -Encoding utf8
$process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.Id | Set-Content -LiteralPath "$EvidencePath/unity-pid.txt"
Write-Output ('组队隔离原生验证 PID=' + $process.Id + '；证据目录 ' + $EvidencePath)
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$exitCode = $null
$resourceStopped = $false
try {
    while (-not $process.WaitForExit(10000)) {
        $free = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024)
        if ($free -lt 768 -or [DateTime]::UtcNow -gt $deadline) {
            # 只操作本函数Start-Process返回的自建进程，保留其它Unity。
            $process.Kill(); $null = $process.WaitForExit(10000); $resourceStopped = $true
            [pscustomobject]@{ownPid=$process.Id;availableMiB=$free;at=[DateTimeOffset]::Now.ToString('o')} |
                ConvertTo-Json | Set-Content -LiteralPath "$EvidencePath/resource-stop.json" -Encoding utf8
            throw '自建隔离Unity触及内存/时间保护，已停止；本轮不能报告通过。'
        }
    }
    $process.Refresh(); $exitCode = $process.ExitCode
} finally {
    $after = Hash-Originals
    $after | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$EvidencePath/source-after.json" -Encoding utf8
    $old = @{}; foreach ($record in $before) { $old[$record.Path] = $record.Sha256 }
    $drift = @($after | Where-Object {$old[$_.Path] -cne $_.Sha256})
    [pscustomobject]@{unityExit=$exitCode;resourceStopped=$resourceStopped;changedSources=$drift;
        sourceFilesUnchangedDuringRun=($drift.Count -eq 0);finishedAt=[DateTimeOffset]::Now.ToString('o')} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$EvidencePath/native-run.json" -Encoding utf8
}
if ($drift.Count) { throw '执行期间原始源码/资源变化，须复验。' }
if (-not (Test-Path -LiteralPath "$EvidencePath/team-tests.xml")) { throw 'Unity未生成测试XML，不能报告通过。' }
[xml]$xml = Get-Content -LiteralPath "$EvidencePath/team-tests.xml" -Raw
Write-Output ('原生结果：总计' + $xml.'test-run'.total + '，通过' + $xml.'test-run'.passed + '，失败' + $xml.'test-run'.failed + '；Unity退出码' + $exitCode)
if (Test-Path -LiteralPath "$EvidencePath/verification.json") { Get-Content -LiteralPath "$EvidencePath/verification.json" -Raw }
if ($exitCode -ne 0) { exit 1 }

[CmdletBinding()]
param(
    [string]$ProjectPath = (Split-Path $PSScriptRoot -Parent),
    [string]$EvidencePath = '',
    [string]$Filter = '',
    [switch]$IncludePlayerFeaturesRegression
)
$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not $EvidencePath) { $EvidencePath = Join-Path $ProjectPath ('.codex-artifacts/activity-tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force -Path $EvidencePath | Out-Null
# 文件恢复测试执行真实原子替换，临时文件也应留在本轮证据目录内。
$activityOriginalTemp = $env:TEMP
$activityOriginalTmp = $env:TMP
$activityTestTemp = Join-Path $EvidencePath 'test-temp'
New-Item -ItemType Directory -Force -Path $activityTestTemp | Out-Null
$env:TEMP = $activityTestTemp
$env:TMP = $activityTestTemp
try {
$priorNames = @('runtime.rsp', 'runtime-compile.log', 'tests.rsp', 'tests-compile.log', 'source-hashes.json', 'results.json', 'ActivityRuntime.dll', 'ActivityTests.dll')
$priorFiles = @($priorNames | ForEach-Object { Join-Path $EvidencePath $_ } | Where-Object { Test-Path -LiteralPath $_ })
if ($priorFiles.Count) {
    $archive = Join-Path $EvidencePath ('previous-runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path $archive | Out-Null
    foreach ($prior in $priorFiles) { Move-Item -LiteralPath $prior -Destination $archive }
}
$compiler = Get-ChildItem 'C:/Program Files/dotnet/sdk' -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'Roslyn/bincore/csc.dll') } |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $compiler) { throw '未找到现有Roslyn编译器，不自动安装工具。' }
$csc = Join-Path $compiler.FullName 'Roslyn/bincore/csc.dll'
[xml]$project = Get-Content -LiteralPath (Join-Path $ProjectPath 'MmorpgClient.csproj') -Raw
$options = @('-nologo', '-target:library', '-langversion:9.0', '-nowarn:0169,0649')
if (@($project.Project.PropertyGroup | Where-Object { $_.AllowUnsafeBlocks -eq 'true' }).Count) { $options += '-unsafe' }
$defines = @($project.Project.PropertyGroup | ForEach-Object { $_.DefineConstants } | Where-Object { $_ } | Select-Object -First 1)
if ($defines.Count) { $options += '-define:' + ($defines[0] -replace ';', ',') }
$references = @($project.Project.ItemGroup | ForEach-Object { $_.Reference } |
    Where-Object { $_ -and $_.HintPath } | ForEach-Object {
        if ([IO.Path]::IsPathRooted($_.HintPath)) { $_.HintPath } else { Join-Path $ProjectPath $_.HintPath }
    })
$missing = @($references | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missing.Count) { throw ('项目现有引用缺失，保留失败：' + ($missing -join ', ')) }
$nunit = Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Library/PackageCache') -Directory |
    Where-Object { $_.Name.StartsWith('com.unity.ext.nunit@') } |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter 'nunit.framework.dll' } | Select-Object -First 1
if (-not $nunit) { throw '缺少项目已缓存的NUnit，不自动下载。' }
$protobuf = $references | Where-Object { [IO.Path]::GetFileName($_) -eq 'Google.Protobuf.dll' } | Select-Object -First 1
if (-not $protobuf) { throw '当前项目没有Google.Protobuf引用。' }
$referenceArgs = @($references | ForEach-Object { '-r:"' + $_ + '"' })
$runtimeSources = @(Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Assets/Scripts') -Recurse -Filter '*.cs' | ForEach-Object FullName)
$testSources = @('Assets/Tests/EditMode/Battle/FakeBattleTransport.cs',
    'Assets/Tests/EditMode/Battle/ActivityVitalityClientTests.cs') | ForEach-Object { Join-Path $ProjectPath $_ }
if ($IncludePlayerFeaturesRegression) { $testSources += Join-Path $ProjectPath 'Assets/Tests/EditMode/Battle/PlayerFeaturesClientTests.cs' }
$hashes = @($runtimeSources + $testSources | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
$hashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidencePath 'source-hashes.json') -Encoding utf8
$runtime = Join-Path $EvidencePath 'ActivityRuntime.dll'
$runtimeRsp = Join-Path $EvidencePath 'runtime.rsp'
$options + $referenceArgs + ('-out:"' + $runtime + '"') + @($runtimeSources | ForEach-Object { '"' + $_ + '"' }) |
    Set-Content -LiteralPath $runtimeRsp -Encoding utf8
# 独立输出目录，不读写client_compile_check的共享TEMP目录，也不启动Unity。
& dotnet exec $csc "@$runtimeRsp" 2>&1 | Tee-Object -FilePath (Join-Path $EvidencePath 'runtime-compile.log')
if ($LASTEXITCODE -ne 0) { throw "运行时编译失败，退出码$LASTEXITCODE。" }
$tests = Join-Path $EvidencePath 'ActivityTests.dll'
$testRsp = Join-Path $EvidencePath 'tests.rsp'
$options + $referenceArgs + ('-r:"' + $runtime + '"') + ('-r:"' + $nunit.FullName + '"') + ('-out:"' + $tests + '"') +
    @($testSources | ForEach-Object { '"' + $_ + '"' }) | Set-Content -LiteralPath $testRsp -Encoding utf8
& dotnet exec $csc "@$testRsp" 2>&1 | Tee-Object -FilePath (Join-Path $EvidencePath 'tests-compile.log')
if ($LASTEXITCODE -ne 0) { throw "测试编译失败，退出码$LASTEXITCODE。" }
foreach ($path in @($protobuf, $nunit.FullName, $runtime)) { [Reflection.Assembly]::LoadFrom($path) | Out-Null }
$assembly = [Reflection.Assembly]::LoadFrom($tests)
$fixtureNames = @('ActivityVitalityClientTests')
if ($IncludePlayerFeaturesRegression) { $fixtureNames += 'PlayerFeaturesClientTests' }
$results = [Collections.Generic.List[object]]::new()
foreach ($fixtureName in $fixtureNames) {
$fixture = $assembly.GetType('MmorpgClient.Tests.EditMode.Battle.' + $fixtureName, $true)
$methods = @($fixture.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Instance'))
$setups = @($methods | Where-Object { $_.IsDefined([NUnit.Framework.SetUpAttribute], $true) } | Sort-Object MetadataToken)
$teardowns = @($methods | Where-Object { $_.IsDefined([NUnit.Framework.TearDownAttribute], $true) } | Sort-Object MetadataToken -Descending)
foreach ($method in $methods) {
    $name = $fixtureName + '.' + $method.Name
    if ($Filter -and $name -notmatch $Filter) { continue }
    $cases = @($method.GetCustomAttributes([NUnit.Framework.TestCaseAttribute], $true))
    if (-not $cases.Count) {
        if (-not $method.IsDefined([NUnit.Framework.TestAttribute], $true)) { continue }
        $cases = @([pscustomobject]@{ Arguments = [object[]]@() })
    }
    foreach ($case in $cases) {
    $instance = [Activator]::CreateInstance($fixture)
    $arguments = [object[]]@($case.Arguments)
    $caseName = if ($arguments.Count) { "$name($($arguments | ConvertTo-Json -Compress))" } else { $name }
    $failure = $null
    try {
        foreach ($setup in $setups) { $setup.Invoke($instance, @()) | Out-Null }
        $method.Invoke($instance, $arguments) | Out-Null
    }
    catch {
        $exception = $_.Exception
        while ($exception.InnerException) { $exception = $exception.InnerException }
        $failure = $exception.ToString()
    }
    finally {
        foreach ($teardown in $teardowns) {
            try { $teardown.Invoke($instance, @()) | Out-Null }
            catch {
                $exception = $_.Exception
                while ($exception.InnerException) { $exception = $exception.InnerException }
                $failure = "$failure 清理失败：$exception".Trim()
            }
        }
    }
    $results.Add([ordered]@{ name = $caseName; fixture = $fixtureName; passed = $null -eq $failure; error = $failure })
    }
}
}
$passed = @($results | Where-Object passed).Count
$changed = @($hashes | Where-Object { (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256 } | ForEach-Object path)
$report = [ordered]@{
    runner = '现有NUnit断言和每例SetUp/TearDown经.NET反射执行；全部运行时真实源码Roslyn编译；未运行Unity或联机。'
    total = $results.Count; passed = $passed; failed = $results.Count - $passed
    sourceFilesUnchangedDuringRun = $changed.Count -eq 0; changedSources = $changed; results = @($results.ToArray())
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidencePath 'results.json') -Encoding utf8
Write-Output "活动领取测试：$passed/$($results.Count)通过。证据：$EvidencePath"
$results | Where-Object { -not $_.passed } | Format-List name,error
if ($results.Count -eq 0 -or $passed -ne $results.Count -or $changed.Count) { exit 1 }
exit 0
}
finally {
    $env:TEMP = $activityOriginalTemp
    $env:TMP = $activityOriginalTmp
}

[CmdletBinding()]
param(
    [string]$ProjectPath = (Split-Path $PSScriptRoot -Parent),
    [string]$EvidencePath = '',
    [string]$Filter = ''
)
$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not $EvidencePath) { $EvidencePath = Join-Path $ProjectPath ('.codex-artifacts/pet-flip-tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force -Path $EvidencePath | Out-Null

$compiler = Get-ChildItem 'C:/Program Files/dotnet/sdk' -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'Roslyn/bincore/csc.dll') } |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $compiler) { throw '未找到现有 Roslyn 编译器，不自动安装工具。' }
$csc = Join-Path $compiler.FullName 'Roslyn/bincore/csc.dll'

$unityRoot = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor' -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'Editor\Unity.exe') } |
    Sort-Object Name -Descending | Select-Object -First 1 | ForEach-Object FullName

[xml]$project = Get-Content -LiteralPath (Join-Path $ProjectPath 'MmorpgClient.csproj') -Raw
$options = @('-nologo', '-target:library', '-langversion:9.0', '-nowarn:0169,0649')
if (@($project.Project.PropertyGroup | Where-Object { $_.AllowUnsafeBlocks -eq 'true' }).Count) { $options += '-unsafe' }
$defines = @($project.Project.PropertyGroup | ForEach-Object { $_.DefineConstants } | Where-Object { $_ } | Select-Object -First 1)
if ($defines.Count) { $options += '-define:' + ($defines[0] -replace ';', ',') }

$references = @($project.Project.ItemGroup | ForEach-Object { $_.Reference } |
    Where-Object { $_ -and $_.HintPath } | ForEach-Object {
        $full = if ([IO.Path]::IsPathRooted($_.HintPath)) { $_.HintPath } else { Join-Path $ProjectPath $_.HintPath }
        if (-not (Test-Path -LiteralPath $full) -and $unityRoot -and $full -match '^(.*\\Unity\\Hub\\Editor\\)[^\\]+\\(.*)$') {
            $cand = Join-Path $unityRoot $Matches[2]
            if (Test-Path -LiteralPath $cand) { $full = $cand }
        }
        $full
    })
$missing = @($references | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missing.Count) { throw ('项目现有引用缺失，保留失败：' + ($missing -join ', ')) }

$nunit = Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Library/PackageCache') -Directory |
    Where-Object { $_.Name.StartsWith('com.unity.ext.nunit@') } |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter 'nunit.framework.dll' } | Select-Object -First 1
if (-not $nunit) { throw '缺少项目已缓存的 NUnit，不自动下载。' }
$protobuf = $references | Where-Object { [IO.Path]::GetFileName($_) -eq 'Google.Protobuf.dll' } | Select-Object -First 1
if (-not $protobuf) { throw '当前项目没有 Google.Protobuf 引用。' }

$referenceArgs = @($references | ForEach-Object { '-r:"' + $_ + '"' })
$runtimeSources = @(Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Assets/Scripts') -Recurse -Filter '*.cs' | ForEach-Object FullName)
$testSources = @(Join-Path $ProjectPath 'Assets/Tests/EditMode/Tianyong/PetFlipClientTests.cs')

$hashes = @($runtimeSources + $testSources | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
$hashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidencePath 'source-hashes.json') -Encoding utf8

$runtime = Join-Path $EvidencePath 'PetFlipRuntime.dll'
$runtimeRsp = Join-Path $EvidencePath 'runtime.rsp'
$options + $referenceArgs + ('-out:"' + $runtime + '"') + @($runtimeSources | ForEach-Object { '"' + $_ + '"' }) |
    Set-Content -LiteralPath $runtimeRsp -Encoding utf8
& dotnet exec $csc "@$runtimeRsp" 2>&1 | Tee-Object -FilePath (Join-Path $EvidencePath 'runtime-compile.log')
if ($LASTEXITCODE -ne 0) { throw "运行时编译失败，退出码 $LASTEXITCODE。" }

$tests = Join-Path $EvidencePath 'PetFlipTests.dll'
$testRsp = Join-Path $EvidencePath 'tests.rsp'
$options + $referenceArgs + ('-r:"' + $runtime + '"') + ('-r:"' + $nunit.FullName + '"') + ('-out:"' + $tests + '"') +
    @($testSources | ForEach-Object { '"' + $_ + '"' }) | Set-Content -LiteralPath $testRsp -Encoding utf8
& dotnet exec $csc "@$testRsp" 2>&1 | Tee-Object -FilePath (Join-Path $EvidencePath 'tests-compile.log')
if ($LASTEXITCODE -ne 0) { throw "测试编译失败，退出码 $LASTEXITCODE。" }

foreach ($path in @($protobuf, $nunit.FullName, $runtime)) { [Reflection.Assembly]::LoadFrom($path) | Out-Null }
$assembly = [Reflection.Assembly]::LoadFrom($tests)
$fixture = $assembly.GetType('MmorpgClient.Tests.EditMode.Tianyong.PetFlipClientTests', $true)
$methods = @($fixture.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Instance'))
$setups = @($methods | Where-Object { $_.IsDefined([NUnit.Framework.SetUpAttribute], $true) } | Sort-Object MetadataToken)
$teardowns = @($methods | Where-Object { $_.IsDefined([NUnit.Framework.TearDownAttribute], $true) } | Sort-Object MetadataToken -Descending)

$results = [Collections.Generic.List[object]]::new()
foreach ($method in $methods) {
    $name = 'PetFlipClientTests.' + $method.Name
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
        $results.Add([pscustomobject]@{ name = $caseName; passed = $null -eq $failure; error = $failure })
    }
}

$passed = @($results | Where-Object passed).Count
$changed = @($hashes | Where-Object { (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256 } | ForEach-Object path)
$report = [ordered]@{
    runner = 'NUnit 断言经 .NET 反射执行；全部运行时真实源码 Roslyn 编译；未运行 Unity 编辑器或联机。'
    total = $results.Count; passed = $passed; failed = $results.Count - $passed
    sourceFilesUnchangedDuringRun = $changed.Count -eq 0; changedSources = $changed; results = @($results.ToArray())
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidencePath 'results.json') -Encoding utf8
Write-Output "灵宠翻牌测试：$passed/$($results.Count) 通过。证据：$EvidencePath"
$results | Where-Object { -not $_.passed } | Format-List name, error
if ($results.Count -eq 0 -or $passed -ne $results.Count -or $changed.Count) { exit 1 }
exit 0

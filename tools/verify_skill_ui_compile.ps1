param([string]$ProjectPath = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
Set-Location -LiteralPath $ProjectPath
$evidence = Join-Path $ProjectPath 'Docs/VerificationEvidence/skills-ui-20261009'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null

# 复用项目当前的 Unity 引用和编译宏；这里不启动编辑器，也不执行渲染测试。
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'client_compile_check.ps1') -ShowErrors 30 2>&1 |
    Tee-Object -FilePath (Join-Path $evidence 'runtime-compile.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$runtimeWork = Join-Path ([IO.Path]::GetTempPath()) 'mmorpg_compile_check'
$skillWork = Join-Path ([IO.Path]::GetTempPath()) 'mmorpg-skill-ui-compile'
New-Item -ItemType Directory -Force -Path $skillWork | Out-Null
Copy-Item -LiteralPath (Join-Path $runtimeWork 'check.dll') -Destination (Join-Path $skillWork 'check.dll')
$options = @(Get-Content -LiteralPath (Join-Path $runtimeWork 'poll.rsp') |
    Where-Object { $_ -notmatch '^"' -and $_ -notmatch '^-out:' })
$compiler = Get-ChildItem 'C:/Program Files/dotnet/sdk' -Directory |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'Roslyn/bincore/csc.dll' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$runtimeReference = '-r:"' + (Join-Path $skillWork 'check.dll') + '"'
$nunit = Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Library/PackageCache') -Recurse -Filter 'nunit.framework.dll' |
    Select-Object -First 1
if (-not $nunit) { throw '缺少项目已有的 NUnit 引用；请先由 Unity 导入项目依赖。' }
$jobs = @(
    @{ Name = 'editor'; Source = 'Assets/Editor/SkillUiVerification.cs'; Extra = @() },
    @{ Name = 'presentation-tests'; Source = 'Assets/Tests/EditMode/Battle/SkillPanelPresentationTests.cs'; Extra = @('-r:"' + $nunit.FullName + '"') }
)
foreach ($job in $jobs) {
    $response = Join-Path $skillWork ($job.Name + '.rsp')
    $assembly = Join-Path $skillWork ($job.Name + '.dll')
    $source = Join-Path $ProjectPath $job.Source
    Set-Content -LiteralPath $response -Encoding utf8 -Value ($options + $runtimeReference + $job.Extra +
        ('-out:"' + $assembly + '"') + ('"' + $source + '"'))
    & dotnet exec $compiler "@$response" 2>&1 | Tee-Object -FilePath (Join-Path $evidence ($job.Name + '-compile.log'))
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Output ($job.Name + ': 编译通过（未执行 Unity 渲染或按钮测试）')
}
@{ status = 'passed'; checkedAtUtc = [DateTime]::UtcNow.ToString('O');
    runtime = 'compiled'; editor = 'compiled'; presentationTests = 'compiled only';
    unityRenderingVerified = $false; onlineLogicVerified = $false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'compile-report.json') -Encoding utf8

<#
.SYNOPSIS
  Exercises client_compile_check.ps1 with the installed real Roslyn compiler.
.DESCRIPTION
  Creates isolated tiny C# projects, including a syntax error and paths containing
  spaces. Keeps a JSON report and child-process logs without requiring Unity.
.EXAMPLE
  pwsh -NoProfile -File tools/tests/test_client_compile_check.ps1
#>
[CmdletBinding()]
param(
    [string]$ScriptPath = (Join-Path $PSScriptRoot '../client_compile_check.ps1'),
    [string]$EvidencePath = '',
    [string]$CscDll = ''
)
$ErrorActionPreference = 'Stop'
$ScriptPath = (Resolve-Path -LiteralPath $ScriptPath).Path
if (-not $EvidencePath) {
    $EvidencePath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) (
        '.codex-artifacts/client-compile-check-tests/run-' + [Guid]::NewGuid().ToString('N'))
}
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
if (Test-Path -LiteralPath $EvidencePath) { throw 'EvidencePath must be a new directory.' }
New-Item -ItemType Directory -Path $EvidencePath | Out-Null
$dotnetRoot = Join-Path $env:ProgramFiles 'dotnet'
if (-not $CscDll) {
    $sdk = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'sdk') -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'Roslyn/bincore/csc.dll') } |
        Sort-Object { [version]($_.Name -split '-')[0] } -Descending | Select-Object -First 1
    if (-not $sdk) { throw 'An installed .NET SDK is required.' }
    $CscDll = Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'
}
$pack = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory |
    Sort-Object { [version]($_.Name -split '-')[0] } -Descending | Select-Object -First 1
$refDir = Get-ChildItem -LiteralPath (Join-Path $pack.FullName 'ref') -Directory | Select-Object -First 1
$runtimeRef = Join-Path $refDir.FullName 'System.Runtime.dll'
if (-not (Test-Path -LiteralPath $runtimeRef)) { throw 'The SDK reference pack is required.' }
$pwsh = (Get-Process -Id $PID).Path
$checks = [Collections.Generic.List[object]]::new()
$children = [Collections.Generic.List[object]]::new()
$artifactRoot = Join-Path $EvidencePath 'compile artifacts'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function New-Fixture([string]$Name, [string]$SourceFile, [string]$Content) {
    $root = Join-Path $EvidencePath ('fixtures/' + $Name)
    $sources = Join-Path $root 'Assets/Scripts'
    New-Item -ItemType Directory -Path $sources | Out-Null
    Set-Content -LiteralPath (Join-Path $sources $SourceFile) -Value $Content -Encoding utf8
    $escapedRef = [Security.SecurityElement]::Escape($runtimeRef)
    @"
<Project><PropertyGroup><LangVersion>9.0</LangVersion></PropertyGroup><ItemGroup>
<Reference Include="System.Runtime"><HintPath>$escapedRef</HintPath></Reference>
</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $root 'MmorpgClient.csproj') -Encoding utf8
    return $root
}
function Start-Compile([string]$Name, [string]$Project, [string[]]$ExtraArgs = @()) {
    $info = [Diagnostics.ProcessStartInfo]::new($pwsh)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-File', $ScriptPath, '-ProjectPath', $Project,
            '-CscDll', $CscDll, '-UnityRoot', $Project) + $ExtraArgs) {
        $info.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($info)
    $child = [pscustomobject]@{ Name = $Name; Project = $Project; Process = $process;
        StdoutTask = $process.StandardOutput.ReadToEndAsync(); StderrTask = $process.StandardError.ReadToEndAsync() }
    $children.Add($child)
    return $child
}
function Complete-Compile($Child) {
    if (-not $Child.Process.WaitForExit(60000)) {
        $Child.Process.Kill($true)
        throw ('Compiler check timed out: ' + $Child.Name)
    }
    $stdout = $Child.StdoutTask.GetAwaiter().GetResult()
    $stderr = $Child.StderrTask.GetAwaiter().GetResult()
    Set-Content -LiteralPath (Join-Path $EvidencePath ($Child.Name + '.stdout.log')) -Value $stdout -Encoding utf8
    Set-Content -LiteralPath (Join-Path $EvidencePath ($Child.Name + '.stderr.log')) -Value $stderr -Encoding utf8
    return [pscustomobject]@{ Name = $Child.Name; Project = $Child.Project;
        ExitCode = $Child.Process.ExitCode; Stdout = $stdout; Stderr = $stderr }
}
function Read-Artifacts($Result) {
    $match = [regex]::Match($Result.Stdout, '(?m)^compile-check artifacts=(.+) retained=(true|false)\r?$')
    Assert-True $match.Success ('Missing isolated artifact record: ' + $Result.Name)
    $path = $match.Groups[1].Value
    Assert-True (Test-Path -LiteralPath $path -PathType Container) ('Missing artifact directory: ' + $path)
    $manifest = Get-Content -LiteralPath (Join-Path $path 'result.json') -Raw | ConvertFrom-Json
    Assert-True ($manifest.projectPath -eq $Result.Project) 'Manifest belongs to the wrong fixture.'
    Assert-True ($manifest.exitCode -eq $Result.ExitCode) 'Manifest exit code does not match the process.'
    Assert-True ($manifest.sourceCount -eq 1) 'Fixture source count must be exactly one.'
    Assert-True (Test-Path -LiteralPath (Join-Path $path 'poll.rsp')) 'Response file is missing.'
    Assert-True (Test-Path -LiteralPath (Join-Path $path 'compiler.log')) 'Compiler log is missing.'
    return $path
}
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; $checks.Add([pscustomobject]@{ name = $Name; passed = $true; error = $null }); Write-Host "PASS $Name" }
    catch { $checks.Add([pscustomobject]@{ name = $Name; passed = $false; error = $_.Exception.Message }); Write-Host "FAIL $Name : $($_.Exception.Message)" }
}

$good = New-Fixture 'good' 'Good.cs' 'public sealed class Good { public int Value => 42; }'
$bad = New-Fixture 'bad' 'Broken.cs' 'public sealed class Broken { public int Value => ; }'
$spaces = New-Fixture 'project with spaces' 'File With Spaces.cs' 'public sealed class Spaces { public string Value => "space path"; }'
try {
    Check 'legacy-success-and-default-cleanup' {
        $result = Complete-Compile (Start-Compile 'legacy' $good)
        Assert-True ($result.ExitCode -eq 0) ('Valid source failed: ' + $result.Stdout + $result.Stderr)
        $record = [regex]::Match($result.Stdout, '(?m)^compile-check artifacts=(.+) retained=false\r?$')
        Assert-True $record.Success 'Default run did not report its unique temporary directory.'
        Assert-True (-not (Test-Path -LiteralPath $record.Groups[1].Value)) 'Default run did not clean up its own temporary directory.'
    }
    Check 'successful-source-keeps-evidence' {
        $result = Complete-Compile (Start-Compile 'success' $good @('-ArtifactDir', $artifactRoot))
        Assert-True ($result.ExitCode -eq 0) ('Valid source failed: ' + $result.Stdout + $result.Stderr)
        $path = Read-Artifacts $result
        Assert-True (Test-Path -LiteralPath (Join-Path $path 'check.dll')) 'Successful compilation did not create an assembly.'
    }
    Check 'syntax-error-preserves-failure' {
        $result = Complete-Compile (Start-Compile 'syntax-error' $bad @('-ArtifactDir', $artifactRoot))
        Assert-True ($result.ExitCode -eq 1) ('Syntax error returned unexpected code: ' + $result.ExitCode)
        $path = Read-Artifacts $result
        $log = Get-Content -LiteralPath (Join-Path $path 'compiler.log') -Raw
        Assert-True ($log -match 'Broken.cs\(' -and $log -match 'error CS') 'The diagnostic does not identify the broken source.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $path 'check.dll'))) 'Failed compilation produced a stale assembly.'
    }
    Check 'project-source-and-artifact-paths-with-spaces' {
        $result = Complete-Compile (Start-Compile 'spaces' $spaces @('-ArtifactDir', $artifactRoot))
        Assert-True ($result.ExitCode -eq 0) ('Space-containing paths failed: ' + $result.Stdout + $result.Stderr)
        $path = Read-Artifacts $result
        $rsp = Get-Content -LiteralPath (Join-Path $path 'poll.rsp') -Raw
        Assert-True ($rsp.Contains('"Assets/Scripts/File With Spaces.cs"')) 'Source path was not preserved and quoted.'
    }
    Check 'concurrent-projects-keep-distinct-results' {
        $first = Start-Compile 'parallel-good' $good @('-ArtifactDir', $artifactRoot)
        $second = Start-Compile 'parallel-bad' $bad @('-ArtifactDir', $artifactRoot)
        $goodResult = Complete-Compile $first
        $badResult = Complete-Compile $second
        Assert-True ($goodResult.ExitCode -eq 0 -and $badResult.ExitCode -eq 1) 'Parallel success/failure results were not preserved.'
        $goodPath = Read-Artifacts $goodResult
        $badPath = Read-Artifacts $badResult
        Assert-True ($goodPath -ne $badPath) 'Parallel processes shared an artifact directory.'
        $goodRsp = Get-Content -LiteralPath (Join-Path $goodPath 'poll.rsp') -Raw
        $badRsp = Get-Content -LiteralPath (Join-Path $badPath 'poll.rsp') -Raw
        Assert-True ($goodRsp.Contains('Good.cs') -and -not $goodRsp.Contains('Broken.cs')) 'The success response file was overwritten.'
        Assert-True ($badRsp.Contains('Broken.cs') -and -not $badRsp.Contains('Good.cs')) 'The failing response file was overwritten.'
        Assert-True (Test-Path -LiteralPath (Join-Path $goodPath 'check.dll')) 'The success assembly was lost.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $badPath 'check.dll'))) 'The failing process inherited a foreign assembly.'
    }
    Check 'keep-artifacts-switch' {
        $result = Complete-Compile (Start-Compile 'keep' $good @('-KeepArtifacts'))
        Assert-True ($result.ExitCode -eq 0) ('KeepArtifacts invocation failed: ' + $result.Stdout + $result.Stderr)
        $null = Read-Artifacts $result
    }
} finally {
    foreach ($child in $children) {
        if (-not $child.Process.HasExited) { $child.Process.Kill($true); $child.Process.WaitForExit() }
        $child.Process.Dispose()
    }
    $failed = @($checks | Where-Object { -not $_.passed }).Count
    [ordered]@{ schemaVersion = 1; scriptPath = $ScriptPath;
        scriptSha256 = (Get-FileHash -LiteralPath $ScriptPath -Algorithm SHA256).Hash.ToLowerInvariant();
        compiler = $CscDll; referenceAssembly = $runtimeRef; tests = $checks.ToArray();
        total = $checks.Count; passed = $checks.Count - $failed; failed = $failed;
        finishedUtc = [DateTimeOffset]::UtcNow.ToString('o') } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidencePath 'test-result.json') -Encoding utf8
}
Write-Host "RESULT total=$($checks.Count) passed=$($checks.Count - $failed) failed=$failed evidence=$EvidencePath"
if ($failed -gt 0) { exit 1 }
exit 0

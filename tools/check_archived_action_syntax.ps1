<#
.SYNOPSIS
  Parse all changed and untracked C# files with Roslyn's C# 9 parser.
.DESCRIPTION
  Writes exact input SHA-256 hashes and syntax diagnostics. This is not Unity type
  checking, compilation, or execution. Requires PowerShell 7 with Roslyn assemblies
  (the bundled Codex PowerShell runtime includes them).
.EXAMPLE
  pwsh -File tools/check_archived_action_syntax.ps1
.EXAMPLE
  pwsh -File tools/check_archived_action_syntax.ps1 -BaseRef HEAD^ -Output D:/work/tmp/qdao-archived-syntax.json
#>
[CmdletBinding()]
param(
    [string]$Project = (Split-Path $PSScriptRoot -Parent),
    [string]$BaseRef = 'HEAD',
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
if (-not $Output) { $Output = Join-Path (Split-Path $Project -Parent) 'tmp/qdao-archived-syntax.json' }
$Output = [IO.Path]::GetFullPath($Output)

if (-not ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) {
    $analysisPath = Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'
    $csharpPath = Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'
    if (-not (Test-Path -LiteralPath $analysisPath) -or -not (Test-Path -LiteralPath $csharpPath)) {
        throw 'Roslyn assemblies were not found. Run with the bundled Codex PowerShell 7 runtime.'
    }
    Add-Type -Path $analysisPath
    Add-Type -Path $csharpPath
}

$tracked = @(& git -C $Project -c core.quotepath=false diff --name-only --diff-filter=ACMR $BaseRef -- '*.cs')
if ($LASTEXITCODE -ne 0) { throw "Cannot enumerate changed C# files against $BaseRef" }
$untracked = @(& git -C $Project -c core.quotepath=false ls-files --others --exclude-standard -- '*.cs')
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate new C# files' }
$paths = @(($tracked + $untracked) | Where-Object { $_ } | Sort-Object -Unique)
if ($paths.Count -eq 0) { throw 'No changed C# files were found. After committing, use -BaseRef HEAD^.' }

$options = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion(
    [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp9).WithPreprocessorSymbols(
        [string[]]@('UNITY_EDITOR', 'UNITY_INCLUDE_TESTS'))
$inputs = @()
$errors = @()
foreach ($relative in $paths) {
    $absolute = Join-Path $Project $relative
    $source = [IO.File]::ReadAllText($absolute)
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        $source, $options, $relative, [Text.Encoding]::UTF8, [Threading.CancellationToken]::None)
    $errors += @($tree.GetDiagnostics() |
        Where-Object { $_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error } |
        ForEach-Object { $_.ToString() })
    $inputs += [ordered]@{
        path = $relative
        sha256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$result = [ordered]@{
    scope = 'Roslyn syntax parsing only; Unity type checking and execution not run'
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    project = $Project
    baseRef = $BaseRef
    languageVersion = 'CSharp9'
    preprocessorSymbols = @('UNITY_EDITOR', 'UNITY_INCLUDE_TESTS')
    roslynAssembly = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree].Assembly.Location
    fileCount = $paths.Count
    syntaxErrorCount = $errors.Count
    errors = $errors
    inputs = $inputs
}
[IO.Directory]::CreateDirectory((Split-Path $Output -Parent)) | Out-Null
[IO.File]::WriteAllText($Output, ($result | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
Write-Output "C# 9 syntax: $($paths.Count) files, $($errors.Count) errors. Evidence: $Output"
if ($errors.Count) { $errors | Write-Output; exit 1 }

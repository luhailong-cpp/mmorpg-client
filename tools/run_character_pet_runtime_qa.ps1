#requires -Version 7.0
<#
.SYNOPSIS
  Prepare/run a real Bootstrap player UI acceptance plan using an isolated local QA account.
.DESCRIPTION
  Default prepares JSON only. -Run starts the explicitly supplied Development player;
  -EditorPlay instead starts the project's exclusive Editor with the real Bootstrap scene.
  The runner does not change PlayerPrefs. The player must contain
  CharacterPetRuntimeVerification. The password comes from a process environment variable.
  Screenshots are Unity ScreenCapture output from the running game, not reference composites.
#>
[CmdletBinding()]
param(
    [string]$PlayerExe,
    [string]$OutputDirectory,
    [string]$Account = ('dev_ui_50fdfb_' + (Get-Date -Format 'MMddHHmmss')),
    [string]$PasswordEnvironment = 'MMORPG_UI_QA_PASSWORD',
    [string]$Gateway = 'http://127.0.0.1:8081',
    [uint32]$Zone = 1,
    [switch]$PrepareDedicatedAccount,
    [switch]$PrepareBaseOnly,
    [switch]$EditorPlay,
    [switch]$IncludeBattle,
    [switch]$ReadOnly,
    [switch]$Run
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if ($Account -notmatch '^(dev_)?ui_50fdfb_[a-zA-Z0-9_]+$') { throw 'Only a dedicated ui_50fdfb_ or dev_ui_50fdfb_ QA account is accepted.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project ('.codex-artifacts/character-pet-ui-50fdfb/runtime-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Evidence directory already exists: $OutputDirectory" }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$steps = [Collections.Generic.List[object]]::new()
function Add-Step([string]$Operation, [string]$Target = '', [string]$Screenshot = '', [float]$Seconds = .6, [bool]$Expected = $false) {
    $steps.Add(@{ operation=$Operation; target=$Target; screenshot=$Screenshot; seconds=$Seconds; expected=$Expected })
}
foreach ($width in @(2560,1920)) {
    $steps.Add(@{operation='resolution';width=$width;height=1080;seconds=1.5})
    Add-Step 'close-panels' -Seconds .5
    Add-Step 'capture' -Screenshot 'scene-background'
    Add-Step 'open-character' -Screenshot '04-character-attributes' -Seconds 1
    Add-Step 'click' 'HealingSources' '04-healing-sources'
    Add-Step 'click' 'CloseSources'
    Add-Step 'click' 'ShieldSources' '04-shield-sources'
    Add-Step 'click' 'CloseSources'
    Add-Step 'click' 'HealingDetail' '05-healing-detail'
    Add-Step 'click' 'EffectSources' '05-healing-sources'
    Add-Step 'click' 'CloseSources'
    Add-Step 'click' 'ShieldSkill' '06-shield-detail'
    Add-Step 'click' 'EffectSources' '06-shield-sources'
    Add-Step 'click' 'CloseSources'
    Add-Step 'close-panels'
    Add-Step 'open-pet' 'EntryPoints' '01-pet-entry-points' 1
    Add-Step 'assert-interactable' 'EntrySavePlate' -Expected $false
    Add-Step 'open-pet' 'FourStats' '02-pet-four-stats' 1
    Add-Step 'assert-interactable' 'ConfirmPlate' -Expected $false
    if (-not $ReadOnly) {
    Add-Step 'click' 'Row0PlusPlate' '02-pet-allocation-draft'
    Add-Step 'assert-interactable' 'ConfirmPlate' -Expected $true
    Add-Step 'click' 'Tab技能Plate' '02-pet-unsaved-dialog'
    Add-Step 'click' 'KeepDraftPlate'
    Add-Step 'snapshot' "before-save-$width"
    Add-Step 'remember-pet-allocation'
    Add-Step 'click' 'ConfirmPlate'
    Add-Step 'wait-idle' -Seconds 1
    Add-Step 'refresh-pets'
    Add-Step 'wait-idle'
    Add-Step 'assert-pet-saved-one-point'
    Add-Step 'snapshot' "after-save-$width"
    Add-Step 'assert-interactable' 'ConfirmPlate' '02-pet-allocation-saved' -Expected $false
    }
    Add-Step 'open-pet' 'Skills' '03-pet-skills' 1.5
    Add-Step 'click' 'PetBadgeExplainPlate' '07-pet-badge-explanation'
    foreach ($badge in 0..4) { Add-Step 'click' "PreviewBadge${badge}Plate" "07-pet-badge-preview-$badge" }
    Add-Step 'click' 'PetBadgeClosePlate'
    Add-Step 'close-panels'
}
if ($IncludeBattle) {
    Add-Step 'close-panels'
    Add-Step 'open-pet' 'FourStats' -Seconds .3
    Add-Step 'click' 'Row0PlusPlate' '08-prebattle-pet-draft' .3
    Add-Step 'close-panels' -Seconds .2
    Add-Step 'auto-battle' -Expected $false
    Add-Step 'start-pve'
    Add-Step 'wait-battle' -Screenshot '08-battle-entry' -Seconds .3
    foreach ($width in @(2560,1920)) {
        $steps.Add(@{operation='resolution';width=$width;height=1080;seconds=.8})
        Add-Step 'assert-battle'
        Add-Step 'click' 'BattlePetDetailsPlate' -Seconds .3
        Add-Step 'open-pet' 'FourStats' '08-pet-battle-read-only' .3
        Add-Step 'assert-interactable' 'ConfirmPlate' -Expected $false
        Add-Step 'assert-interactable' 'Row0PlusPlate' -Expected $false
        $steps.Add(@{operation='assert-text';text='+1';seconds=.1})
        Add-Step 'click' 'CultivationClosePlate' -Seconds .3
        Add-Step 'click' 'BattleCharacterDetailsPlate' '08-character-battle-read-only' .3
        Add-Step 'close-panels' -Seconds .2
    }
    Add-Step 'auto-battle' -Expected $true
    Add-Step 'wait-battle-end' -Seconds .5
    Add-Step 'auto-battle' -Expected $false
    Add-Step 'return-to-scene' -Screenshot '08-returned-to-scene' -Seconds .2
}
$plan = [ordered]@{
    outputDirectory=$OutputDirectory;account=$Account;passwordEnvironment=$PasswordEnvironment;
    zone=$Zone;prepareDedicatedAccount=($PrepareDedicatedAccount.IsPresent -or $PrepareBaseOnly.IsPresent);level=60;petTableId=1;
    characterSkills=$(if($PrepareBaseOnly){@()}else{@(10010,10040)});petSkills=$(if($PrepareBaseOnly){@()}else{@(10010)});width=2560;height=1080;steps=$steps.ToArray()
}
if ($PrepareBaseOnly) { $plan.characterSkills=@(); $plan.petSkills=@() }
$planPath = Join-Path $OutputDirectory 'plan.json'
$plan | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $planPath -Encoding utf8NoBOM
$provenance = [ordered]@{
    preparedUtc=[DateTime]::UtcNow.ToString('O');source=$(if($EditorPlay){'normal Bootstrap Editor Play; local live server'}else{'normal Bootstrap Development player; local live server'});
    gateway=$Gateway;account=$Account;planSha256=(Get-FileHash -LiteralPath $planPath).Hash;
    exactReferenceMatchClaimed=$false;pendingCoverage=@('visual review','battle read-only lock','source popup content review');
    revision=(& git -C $project rev-parse HEAD);dirtyStatus=@(& git -C $project status --short)
}
$sources = @('Assets/Scripts/App/CharacterPetRuntimeVerification.cs','Assets/Scripts/UI/Ugui/Attribute/CharacterSupportWindow.cs',
    'Assets/Scripts/UI/Ugui/Attribute/AttributeUiRoot.cs','Assets/Scripts/UI/Ugui/Pet/PetUiRoot.cs',
    'Assets/Scripts/UI/Ugui/Pet/PetCultivationPanel.cs','Assets/Scripts/UI/Ugui/Pet/PetCultivationPanel.Skills.cs')
$provenance.sourceHashes = @($sources | ForEach-Object {
    $path=Join-Path $project $_
    if (Test-Path -LiteralPath $path) { @{path=$_;sha256=(Get-FileHash -LiteralPath $path).Hash} }
})
$provenancePath = Join-Path $OutputDirectory 'provenance.json'
$provenance | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $provenancePath -Encoding utf8NoBOM
Write-Output "Prepared plan: $planPath"
if (-not $Run) { return }
if (Get-Process -Name Unity -ErrorAction SilentlyContinue) { throw 'Unity Editor is running. Coordinate the exclusive verification window first.' }
if (Get-Process -Name mmorpg -ErrorAction SilentlyContinue) { throw 'A game client is already running; do not disturb that session.' }
if (-not [Environment]::GetEnvironmentVariable($PasswordEnvironment)) { throw "Set $PasswordEnvironment for the dedicated QA account before -Run." }
if ($EditorPlay) {
    $PlayerExe=Join-Path (Split-Path $project -Parent) 'tools/Unity/6000.6.0f1/Editor/Unity.exe'
    if(-not(Test-Path -LiteralPath $PlayerExe)){throw 'The project Unity Editor was not found.'}
    $provenance.editorExecutable=$PlayerExe
} else {
    if (-not $PlayerExe -or -not (Test-Path -LiteralPath $PlayerExe -PathType Leaf)) { throw '-Run requires the newly built Development PlayerExe.' }
    $PlayerExe = (Resolve-Path -LiteralPath $PlayerExe).Path
    $manifestPath = Join-Path (Split-Path $PlayerExe -Parent) 'build-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Normal player build manifest is required.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if (-not $manifest.development -or $manifest.result -ne 'Succeeded') { throw 'A successful explicit Development build is required for this QA harness.' }
    $provenance.playerSha256=(Get-FileHash -LiteralPath $PlayerExe).Hash
    $provenance.playerManifest=$manifest
}
$provenance | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $provenancePath -Encoding utf8NoBOM
$launch = [Diagnostics.ProcessStartInfo]::new($PlayerExe)
$launch.UseShellExecute=$false
$launch.CreateNoWindow=$true
$launch.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
$launch.Environment['MMORPG_AUTOPILOT_ARGS']=''
foreach ($argument in @('-manual-play','-gateway',$Gateway,'-characterPetQaPlan',$planPath,
    '-screen-width','2560','-screen-height','1080','-screen-fullscreen','0',
    '-logFile',(Join-Path $OutputDirectory 'player.log'))) { $launch.ArgumentList.Add($argument) }
if ($EditorPlay) {
    foreach($argument in @('-projectPath',$project,'-executeMethod','MmorpgClient.Editor.CharacterPetRuntimePlayVerification.Run',
        '-job-worker-count','2','-desiredWorkerCount','1')) { $launch.ArgumentList.Add($argument) }
}
$player = [Diagnostics.Process]::Start($launch)
@{pid=$player.Id;startedUtc=[DateTime]::UtcNow.ToString('O');evidence=$OutputDirectory} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'process.json') -Encoding utf8NoBOM
Write-Output "Started dedicated QA player PID=$($player.Id). Evidence: $OutputDirectory"
# Intentionally return immediately: the coordinator polls the report while continuing to communicate.

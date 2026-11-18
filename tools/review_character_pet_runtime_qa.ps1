#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory,[switch]$ReadOnly)
$ErrorActionPreference='Stop'
$EvidenceDirectory=(Resolve-Path -LiteralPath $EvidenceDirectory).Path
$report=Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'runtime-report.json') -Raw | ConvertFrom-Json
$checks=[Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed,[string]$Detail='') { $checks.Add(@{name=$Name;passed=$Passed;detail=$Detail}) }
Check 'connected live session' ($report.liveServerVerified -and [uint64]$report.playerId -gt 0)
Check 'runner completed without a failure' ($report.result -eq 'captured; visual review pending') $report.error
Check 'no runtime error/exception logs' (@($report.runtimeErrors).Count -eq 0) (@($report.runtimeErrors).Count.ToString())
$pages=@('01-pet-entry-points','02-pet-four-stats','03-pet-skills','04-character-attributes','05-healing-detail','06-shield-detail')
$overlays=@('04-healing-sources','04-shield-sources','05-healing-sources','06-shield-sources','02-pet-unsaved-dialog','07-pet-badge-explanation')
if($ReadOnly) {$overlays=@($overlays | Where-Object {$_ -ne '02-pet-unsaved-dialog'})}
foreach($width in @(2560,1920)) {
    $extra=@('scene-background'); if(-not $ReadOnly){$extra+='02-pet-allocation-saved'}
    foreach($page in $pages + $overlays + $extra) {
        $file="$page-${width}x1080.png"
        $shot=@($report.screenshots | Where-Object {$_.file -eq $file})
        Check $file ($shot.Count -eq 1 -and $shot[0].width -eq $width -and $shot[0].height -eq 1080 -and $shot[0].inGame -and $shot[0].gateReady)
    }
    if($ReadOnly){continue}
    $beforePath=Join-Path $EvidenceDirectory "before-save-$width-pets.json"
    $afterPath=Join-Path $EvidenceDirectory "after-save-$width-pets.json"
    if((Test-Path -LiteralPath $beforePath) -and (Test-Path -LiteralPath $afterPath)) {
        $before=Get-Content -LiteralPath $beforePath -Raw | ConvertFrom-Json
        $after=Get-Content -LiteralPath $afterPath -Raw | ConvertFrom-Json
        $first=$before.pets[0]
        $saved=@($after.pets | Where-Object {$_.petId -eq $first.petId})
        $oldTotal=($first.dimensions | ForEach-Object {[long]$_.allocated} | Measure-Object -Sum).Sum
        $newTotal=($saved[0].dimensions | ForEach-Object {[long]$_.allocated} | Measure-Object -Sum).Sum
        Check "live pet one-point save $width" ($saved.Count -eq 1 -and [long]$saved[0].remainingPoints+1 -eq [long]$first.remainingPoints -and $newTotal -eq $oldTotal+1)
    } else { Check "live pet one-point save $width" $false 'Missing authoritative before/after snapshots' }
}
foreach($shot in $report.screenshots) {
    $path=Join-Path $EvidenceDirectory $shot.file
    $actual=if(Test-Path -LiteralPath $path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}else{''}
    Check ("SHA256 "+$shot.file) ($actual -eq $shot.sha256)
}
$planPath=Join-Path $EvidenceDirectory 'plan.json'
$battleRequested=$false
if(Test-Path -LiteralPath $planPath) {
    $plan=Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
    $battleRequested=@($plan.steps | Where-Object {$_.operation -eq 'start-pve'}).Count -gt 0
}
if($battleRequested) {
    foreach($width in @(2560,1920)) {
        foreach($subject in @('pet','character')) {
            $file="08-$subject-battle-read-only-${width}x1080.png"
            $shot=@($report.screenshots | Where-Object {$_.file -eq $file})
            Check "live battle $subject $width" ($shot.Count -eq 1 -and $shot[0].inGame -and $shot[0].gateReady -and
                $shot[0].battlePhase -in @('WaitingAction','Resolving'))
            if($subject -eq 'pet' -and $shot.Count -eq 1) {
                Check "battle retained draft $width" (@($shot[0].visibleText | Where-Object {$_ -eq '+1'}).Count -gt 0)
                foreach($control in @('ConfirmPlate','Row0PlusPlate','Row0MinusPlate','DiscardPlate')) {
                    $button=@($shot[0].buttons | Where-Object {$_.path.EndsWith('/'+$control)})
                    Check "battle disabled $control $width" ($button.Count -eq 1 -and -not $button[0].interactable)
                }
            }
            if($subject -eq 'character' -and $shot.Count -eq 1) {
                $allocation=@($shot[0].buttons | Where-Object {$_.path.EndsWith('/OverviewPage/Allocation')})
                $sources=@($shot[0].buttons | Where-Object {$_.path -match '/SupportEffects/(Healing|Shield)Sources$'})
                Check "character allocation locked $width" ($allocation.Count -eq 1 -and -not $allocation[0].interactable)
                Check "character sources remain readable $width" ($sources.Count -eq 2 -and @($sources | Where-Object {-not $_.interactable}).Count -eq 0)
            }
        }
    }
    Check 'normal auto battle completion was awaited' (@($report.actions | Where-Object {$_ -match 'wait-battle-end'}).Count -eq 1 -and
        $report.result -eq 'captured; visual review pending')
    $returned=@($report.screenshots | Where-Object {$_.file -like '08-returned-to-scene-*'})
    Check 'normal result confirmation returned to live scene' ($returned.Count -eq 1 -and $returned[0].inGame -and
        $returned[0].gateReady -and $returned[0].battlePhase -eq 'None' -and
        @($returned[0].buttons | Where-Object {$_.path -like '*BattleResultPanel/ConfirmPlate'}).Count -eq 0)
}
$result=[ordered]@{
    reviewedUtc=[DateTime]::UtcNow.ToString('O');evidence=$EvidenceDirectory;
    functionalEvidencePassed=(@($checks | Where-Object {-not $_.passed}).Count -eq 0);
    visualReview='Not automated; inspect full native screenshots';exactReferenceMatchClaimed=$false;
    battleCoverage=$(if($battleRequested){'Real PVE requested; see live phase and control checks'}else{'Not requested in this run'});checks=$checks.ToArray()
}
$out=Join-Path $EvidenceDirectory 'functional-review.json'
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $out -Encoding utf8NoBOM
Write-Output ("Functional checks: {0}/{1}; report={2}" -f @($checks | Where-Object {$_.passed}).Count,$checks.Count,$out)
if(-not $result.functionalEvidencePassed) { $checks | Where-Object {-not $_.passed} | ForEach-Object {Write-Output ("FAILED: "+$_.name+" "+$_.detail)};exit 1 }

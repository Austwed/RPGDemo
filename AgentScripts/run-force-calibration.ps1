[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,
    [string] $Cases = 'flat,hideout,defense,bandit,second,elite,captain3,captain5',
    [string] $Parties = '0,1,2',
    [string] $Grades = '0,1,2',
    [ValidateSet('train','holdout')] [string] $Split = 'train',
    [ValidatePattern('^[a-zA-Z0-9._-]+$')] [string] $Version = 'current',
    [ValidatePattern('^[a-zA-Z0-9._-]+$')] [string] $RunId = 'current-train',
    [ValidateRange(1,128)] [int] $Workers = 4,
    [ValidateRange(1,10000)] [int] $Count = 32,
    [ValidateRange(0,99999)] [int] $Start = 0,
    [string] $Rules = 'Assets/Resources/DemoBalance/DemoBalance.json'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$projectVersion = Get-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt') |
    Select-String '^m_EditorVersion: (.+)$' | Select-Object -First 1
$editorVersion = $projectVersion.Matches[0].Groups[1].Value
$editor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$editorVersion/Editor/Unity.exe"
$outputDirectory = Join-Path $project "Logs/p54-calibration/$RunId"
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$log = Join-Path $outputDirectory 'unity.log'
$arguments = @('-batchmode','-nographics','-projectPath', ('"' + $project + '"'), '-logFile', ('"' + $log + '"'),
    '-executeMethod','BaseConquest.Editor.PowerCalibration.RunFourProfession', "--version=$Version", "--cases=$Cases",
    "--parties=$Parties", "--grades=$Grades", "--split=$Split", "--count=$Count", "--start=$Start", "--workers=$Workers",
    ('"--rules=' + $Rules + '"'), ('"--out=' + $outputDirectory + '"'))
$process = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern '\[ForceCalibration\] PASS' -Quiet)) {
    throw "Unity 战力标定失败（退出码 $($process.ExitCode)），详见 $log"
}
Write-Output $outputDirectory

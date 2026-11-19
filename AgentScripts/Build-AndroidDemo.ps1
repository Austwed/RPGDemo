param(
    [string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Unity.exe',
    [string]$ApkPath = 'Builds/Android/DEMO.apk',
    [switch]$Development,
    [switch]$LanDuelTest,
    [switch]$IncludeDevelopmentAssets
)

$ErrorActionPreference = 'Stop'
if ($LanDuelTest -and !$PSBoundParameters.ContainsKey('ApkPath')) { $ApkPath = 'Logs/AndroidQa/lan-duel-qa.apk' }
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (![IO.Path]::IsPathRooted($ApkPath)) { $ApkPath = Join-Path $project $ApkPath }
$output = [IO.Path]::GetFullPath($ApkPath)
$log = [IO.Path]::ChangeExtension($output, '.unity.log')
$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if ($temp -match '[^\u0000-\u007F]') { throw 'Android requires an ASCII temporary path.' }
$stage = Join-Path $temp ('BaseConquestAndroid-' + [Guid]::NewGuid().ToString('N'))
$stagedApk = Join-Path $stage 'DEMO.apk'
$stagedLog = Join-Path $stage 'android-build.log'
$succeeded = $false
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
[IO.Directory]::CreateDirectory($stage) | Out-Null

try {
    foreach ($directory in @('Assets', 'Packages', 'ProjectSettings')) {
        $source = Join-Path $project $directory
        $destination = Join-Path $stage $directory
        $copyArgs = @($source, $destination, '/E', '/R:1', '/W:1', '/MT:8', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
        if ($directory -eq 'Assets' -and !$Development -and !$IncludeDevelopmentAssets) {
            $copyArgs += @('/XD', (Join-Path $source 'StreamingAssets/StoryScene'))
            if (!$LanDuelTest) { $copyArgs += (Join-Path $source 'Resources/DemoStages') }
            $copyArgs += @('/XF', 'StoryScene.meta', 'PerformanceTestRunInfo.json*', 'PerformanceTestRunSettings.json*')
            if (!$LanDuelTest) { $copyArgs += 'DemoStages.meta' }
            # These four legacy textures are only fallbacks for art present in PixelDemo.
            foreach ($name in @('Grass-v1', 'Sand-v1', 'Road-v1', 'Notice-v1')) {
                $texture = Join-Path $source "Resources/MapSurfaces/$name.png"
                $copyArgs += @($texture, "$texture.meta")
            }
        }
        & robocopy.exe @copyArgs
        if ($LASTEXITCODE -ge 8) { throw "Could not copy $directory (Robocopy $LASTEXITCODE)." }
    }
    if (!$Development -and !$IncludeDevelopmentAssets) {
        $manifestPath = Join-Path $stage 'Packages/manifest.json'
        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
        foreach ($package in @('com.unity.ai.assistant', 'com.unity.ai.inference')) {
            $manifest.dependencies.PSObject.Properties.Remove($package)
        }
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10))
    }

    $unityArgs = @('-batchmode', '-nographics', '-buildTarget', 'Android', '-projectPath', ('"' + $stage + '"'),
        '-executeMethod', 'BaseConquest.Editor.BuildAndroidDemo.Build', '-demoApkPath', ('"' + $stagedApk + '"'),
        '-logFile', ('"' + $stagedLog + '"'))
    if ($Development) { $unityArgs += '-demoDevelopment' }
    if ($LanDuelTest) { $unityArgs += '-demoLanDuelTest' }
    Write-Output "Building Android in $stage"
    $unity = Start-Process -FilePath $UnityEditor -ArgumentList $unityArgs -WorkingDirectory $stage -WindowStyle Hidden -Wait -PassThru
    if ($unity.ExitCode -ne 0 -or !(Test-Path -LiteralPath $stagedApk -PathType Leaf)) {
        throw "Unity exited with $($unity.ExitCode). The build directory is $stage."
    }
    Copy-Item -LiteralPath $stagedApk -Destination $output -Force
    Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($stagedApk, '.assets.tsv')) -Destination ([IO.Path]::ChangeExtension($output, '.assets.tsv')) -Force
    $succeeded = $true
    Get-Item -LiteralPath $output | Select-Object FullName, Length
}
finally {
    if (Test-Path -LiteralPath $stagedLog -PathType Leaf) { Copy-Item -LiteralPath $stagedLog -Destination $log -Force }
    if ($succeeded) {
        $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
        if ([IO.Path]::GetDirectoryName($resolvedStage).TrimEnd('\') -eq $temp.TrimEnd('\') -and
            [IO.Path]::GetFileName($resolvedStage) -match '^BaseConquestAndroid-[a-f0-9]{32}$') {
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force
        }
    }
}

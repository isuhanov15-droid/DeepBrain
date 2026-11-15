$ErrorActionPreference='Stop'
$deepbrainRoot=Join-Path $env:LOCALAPPDATA 'DeepBrain'
$deepbrainWeights=Join-Path $deepbrainRoot 'downloads\qwen35-9b-verified.gguf'
$deepbrainExpected='02d45dc1cf451ba2475ac33b301c2dd8f985abe4c182ce04a1f2f5bf0260278d'
$deepbrainDeadline=(Get-Date).AddMinutes(30)
while (!(Test-Path -LiteralPath $deepbrainWeights)) {
    if ((Get-Date) -gt $deepbrainDeadline) { throw 'Model download is not finished' }
    Start-Sleep -Seconds 5
}
if ((Get-FileHash -LiteralPath $deepbrainWeights -Algorithm SHA256).Hash.ToLowerInvariant() -ne $deepbrainExpected) { throw 'Model checksum mismatch' }
$deepbrainBlob=Join-Path $env:USERPROFILE ('.ollama\models\blobs\sha256-'+$deepbrainExpected)
if (!(Test-Path -LiteralPath $deepbrainBlob)) {
    Copy-Item -LiteralPath $deepbrainWeights -Destination ($deepbrainBlob+'.deepbrain-new')
    Move-Item -LiteralPath ($deepbrainBlob+'.deepbrain-new') -Destination $deepbrainBlob
}
if ((Get-FileHash -LiteralPath $deepbrainBlob -Algorithm SHA256).Hash.ToLowerInvariant() -ne $deepbrainExpected) { throw 'Installed model checksum mismatch' }
Write-Output 'Verified official Qwen weights installed; registering model'
py -3 -X utf8 (Join-Path $PSScriptRoot 'pull_local_qwen.py')
if ($LASTEXITCODE -ne 0) { throw 'Qwen registration failed' }
py -3 -X utf8 (Join-Path $PSScriptRoot 'probe_local_qwen.py')
if ($LASTEXITCODE -ne 0) { throw 'Real Qwen response check failed' }
Write-Output 'Local Qwen response verified; ready for Web deployment'

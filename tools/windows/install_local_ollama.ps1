$ErrorActionPreference = 'Stop'
$deepbrainRoot = Join-Path $env:LOCALAPPDATA 'DeepBrain'
$deepbrainZip = Join-Path $deepbrainRoot 'downloads\ollama-windows-amd64-v0.40.16.zip'
$deepbrainChecksum = Get-Content (Join-Path $deepbrainRoot 'downloads\sha256sum-v0.40.16.txt') | Where-Object { $_.EndsWith(' ./ollama-windows-amd64.zip') }
$deepbrainExpected = ($deepbrainChecksum -split '\s+')[0]
if ((Get-FileHash -LiteralPath $deepbrainZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $deepbrainExpected) { throw 'Download is incomplete or checksum differs' }
$deepbrainInstall = Join-Path $deepbrainRoot 'ollama'
if (Test-Path -LiteralPath $deepbrainInstall) { throw 'Ollama destination exists; inspect before overwriting' }
Expand-Archive -LiteralPath $deepbrainZip -DestinationPath $deepbrainInstall
$deepbrainExe = Join-Path $deepbrainInstall 'ollama.exe'
if (!(Test-Path -LiteralPath $deepbrainExe)) { throw 'Ollama executable missing' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'qwen_supervisor.py') -Destination (Join-Path $deepbrainRoot 'qwen_supervisor.py')
$deepbrainPython = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python313\pythonw.exe'
$deepbrainStartup = [Environment]::GetFolderPath('Startup')
$deepbrainShell = New-Object -ComObject WScript.Shell
$deepbrainShortcut = $deepbrainShell.CreateShortcut((Join-Path $deepbrainStartup 'DeepBrain Qwen.lnk'))
$deepbrainShortcut.TargetPath = $deepbrainPython
$deepbrainShortcut.Arguments = '"' + (Join-Path $deepbrainRoot 'qwen_supervisor.py') + '"'
$deepbrainShortcut.WorkingDirectory = $deepbrainRoot
$deepbrainShortcut.WindowStyle = 7
$deepbrainShortcut.Description = 'Локальный диалог Qwen для DeepBrain'
$deepbrainShortcut.Save()
Copy-Item -LiteralPath (Join-Path $deepbrainStartup 'DeepBrain Qwen.lnk') -Destination (Join-Path $deepbrainRoot 'DeepBrain Qwen.lnk')
$deepbrainLauncher = Start-Process -FilePath $deepbrainPython -ArgumentList @(('"' + (Join-Path $deepbrainRoot 'qwen_supervisor.py') + '"')) -WorkingDirectory $deepbrainRoot -WindowStyle Hidden -PassThru
Write-Output ('Supervisor started: '+$deepbrainLauncher.Id)

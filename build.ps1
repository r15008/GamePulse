param([string]$Output = "$PSScriptRoot\dist")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$references = @('System.dll','System.Core.dll','System.Management.dll','System.Web.Extensions.dll','System.Xaml.dll','System.Xml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path "$framework\WPF" $_) }
$sources = Get-ChildItem -LiteralPath "$PSScriptRoot\src" -Filter '*.cs' | ForEach-Object FullName
$argsList = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',"/out:$Output\GamePulse.exe","/win32manifest:$PSScriptRoot\app.manifest","/win32icon:$PSScriptRoot\src\GamePulse.ico","/resource:$PSScriptRoot\src\Theme.xaml,Theme.xaml","/resource:$PSScriptRoot\THIRD-PARTY-NOTICES.txt,ThirdPartyNotices.txt","/resource:$PSScriptRoot\vendor\PresentMon.exe,PresentMon.exe") + $references + $sources
& "$framework\csc.exe" @argsList
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath "$PSScriptRoot\GamePulse.exe.config" -Destination $Output -Force
Write-Output "Built $Output\GamePulse.exe"

$ErrorActionPreference = 'Stop'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')
$nextPath = Join-Path $PSScriptRoot 'PixelForge.build.exe'
$mainPath = Join-Path $PSScriptRoot 'PixelForge.exe'
$compilerArgs = @('/nologo', '/target:winexe', '/optimize+', '/platform:x64', "/out:$nextPath")
foreach ($reference in $references) { $compilerArgs += "/reference:$framework\$reference" }
$compilerArgs += (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& "$framework\csc.exe" @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
if (Test-Path -LiteralPath $mainPath) {
    $backupDirectory = Join-Path $PSScriptRoot 'backups'
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $backupPath = Join-Path $backupDirectory ('PixelForge-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.exe')
    Move-Item -LiteralPath $mainPath -Destination $backupPath -ErrorAction Stop
}
Move-Item -LiteralPath $nextPath -Destination $mainPath -ErrorAction Stop
Write-Output "Built $PSScriptRoot\PixelForge.exe"


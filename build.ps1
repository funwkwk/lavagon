# Builds Lavagon and assembles a portable folder in dist\Lavagon
#   powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist\Lavagon'

dotnet build (Join-Path $root 'src\Lavagon') -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $root 'src\Lavagon\bin\Release\Lavagon.exe') $dist
Copy-Item (Join-Path $root 'LICENSE') $dist

foreach ($tool in 'apache', 'php', 'mysql', 'nginx', 'redis', 'mailpit', 'nodejs', 'python', 'postgresql', 'mongodb', 'memcached') {
    New-Item -ItemType Directory -Force (Join-Path $dist "bin\$tool") | Out-Null
}
New-Item -ItemType Directory -Force (Join-Path $dist 'www') | Out-Null
Copy-Item (Join-Path $root 'docs\bin-README.txt') (Join-Path $dist 'bin\README.txt')
Set-Content (Join-Path $dist 'www\index.php') '<?php phpinfo();'

Write-Host "Portable Lavagon ready in $dist"

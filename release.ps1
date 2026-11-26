# Builds Lavagon and publishes a GitHub release from this PC (no GitHub Actions needed).
#
#   powershell -ExecutionPolicy Bypass -File release.ps1 -Version 1.0.1
#
# Requirements: .NET SDK, git, GitHub CLI logged in (gh auth login) with push rights.
# Run it on the 'main' branch after merging 'dev' into it.
# Steps: check a clean working tree in sync with origin, set the version in the project,
# build, installer + zip + SHA256, commit the version bump, tag vX.Y.Z, push, create (or update) the release.
# -DryRun builds the zip in dist\ and changes nothing in git or on GitHub.
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$NotesFile,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$tag = "v$Version"
Set-Location $root

function Invoke-Checked([string]$what, [scriptblock]$cmd) {
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

# 1. Preconditions
if (git status --porcelain) { throw 'Working tree is not clean. Commit or stash your changes first.' }
Invoke-Checked 'git fetch' { git fetch --tags -q origin }
$branch = git rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') { throw "Releases are made from 'main' (current branch: $branch). Merge dev into main first: git checkout main; git merge --no-ff dev" }
if ((git rev-parse HEAD) -ne (git rev-parse "origin/$branch")) { throw "Branch $branch is not in sync with origin/$branch." }
if (git tag --list $tag) { throw "Tag $tag already exists." }
Invoke-Checked 'gh auth status' { gh auth status *> $null }

# 2. Version bump (csproj + Program.cs)
$csproj = Join-Path $root 'src\Lavagon\Lavagon.csproj'
$program = Join-Path $root 'src\Lavagon\Program.cs'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($csproj, ([IO.File]::ReadAllText($csproj) -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"), $utf8)
[IO.File]::WriteAllText($program, ([IO.File]::ReadAllText($program) -replace 'Version = "[^"]+"', "Version = `"$Version`""), $utf8)

# 3. Build, zip, checksum
Invoke-Checked 'build' { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') }
$out = Join-Path $root 'dist'
$zip = Join-Path $out "Lavagon-$tag-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out 'Lavagon') -DestinationPath $zip
$setup = Join-Path $out "Lavagon-Setup-$tag.exe"
Copy-Item (Join-Path $out 'Lavagon-Setup.exe') $setup -Force
$assets = @($setup, $zip)
foreach ($f in $assets) {
    $hash = (Get-FileHash $f -Algorithm SHA256).Hash.ToLower()
    Set-Content "$f.sha256" "$hash  $(Split-Path $f -Leaf)" -NoNewline -Encoding ASCII
    Write-Host "Built $f ($hash)"
}
$assets += $assets | ForEach-Object { "$_.sha256" }

if ($DryRun) {
    git checkout -q -- $csproj $program
    Write-Host 'Dry run: version files restored, nothing committed, pushed or published.'
    return
}

# 4. Commit the version bump, tag, push, publish
if (git status --porcelain) {
    git add $csproj $program
    Invoke-Checked 'git commit' { git commit -q -m "Release $tag" }
}
Invoke-Checked 'git tag' { git tag -a $tag -m "Lavagon $tag" }
# Use the GitHub CLI login for this push only (ignores other stored Git credentials).
Invoke-Checked 'git push' { git -c credential.helper= -c 'credential.helper=!gh auth git-credential' push origin $branch $tag }
$notes = if ($NotesFile) { @('--notes-file', $NotesFile) } else { @('--generate-notes') }
gh release view $tag *> $null
if ($LASTEXITCODE -eq 0) {
    Invoke-Checked 'gh release upload' { gh release upload $tag @assets --clobber }
} else {
    Invoke-Checked 'gh release create' { gh release create $tag @assets --title "Lavagon $tag" --latest @notes }
}
Write-Host "Published https://github.com/funwkwk/lavagon/releases/tag/$tag"
Write-Host "Now bring the version bump back to dev: git checkout dev; git merge main; git push"

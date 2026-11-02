<#
.SYNOPSIS
    Builds the DOrc Notifications Teams app package.

.DESCRIPTION
    Substitutes the bot's Azure AD application id into manifest.json and zips the
    manifest with its icons, ready for upload to the tenant app catalog.

    The app id is NOT stored in this repository - it is the "Application (client) ID"
    GUID from the bot's Azure AD app registration (Azure Portal -> App registrations
    -> the DOrc notification bot -> Overview). It is the same value configured as
    TeamsNotification:BotAppId / the TEAMS.BOT.APP.ID MSI parameter on the Monitor.

.PARAMETER BotAppId
    The bot's Azure AD application (client) id, as a GUID.

.PARAMETER OutputPath
    Where to write the .zip. Defaults to dorc-notifications.zip alongside this script.

.EXAMPLE
    ./package.ps1 -BotAppId 00000000-0000-0000-0000-000000000000
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BotAppId,

    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

$parsedAppId = [Guid]::Empty
if (-not [Guid]::TryParse($BotAppId, [ref] $parsedAppId)) {
    throw "BotAppId '$BotAppId' is not a valid GUID. Use the Application (client) ID from the bot's Azure AD app registration."
}

$appId = $parsedAppId.ToString()
$scriptRoot = $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $scriptRoot 'dorc-notifications.zip' }

# These names are fixed: manifest.json's icons block refers to them literally, and
# Teams resolves them by name inside the zip. Renaming an icon breaks the upload.
$sourceFiles = 'manifest.json', 'color.png', 'outline.png'
foreach ($file in $sourceFiles) {
    $path = Join-Path $scriptRoot $file
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file '$file' was not found in '$scriptRoot'. The icon files must be named exactly 'color.png' and 'outline.png'."
    }
}

# Icon checks. Teams rejects wrong dimensions outright, and renders a non-transparent
# outline icon as a solid block in the app bar - both are easier to catch here than
# after a catalog upload.
function Test-TeamsIcons {
    param([string] $Root)

    try { Add-Type -AssemblyName System.Drawing -ErrorAction Stop }
    catch {
        Write-Warning "System.Drawing unavailable - skipping icon validation. Verify the icons by hand."
        return
    }

    $expected = @{ 'color.png' = 192; 'outline.png' = 32 }
    foreach ($name in $expected.Keys) {
        $size = $expected[$name]
        $bitmap = [System.Drawing.Bitmap]::FromFile((Join-Path $Root $name))
        try {
            if ($bitmap.Width -ne $size -or $bitmap.Height -ne $size) {
                throw "$name must be ${size}x${size}, but is $($bitmap.Width)x$($bitmap.Height)."
            }

            $transparent = 0
            $colouredVisible = 0
            $distinct = @{}
            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                for ($x = 0; $x -lt $bitmap.Width; $x++) {
                    $pixel = $bitmap.GetPixel($x, $y)
                    $distinct[$pixel.ToArgb()] = $true
                    if ($pixel.A -eq 0) { $transparent++ }
                    elseif ($pixel.R -ne 255 -or $pixel.G -ne 255 -or $pixel.B -ne 255) { $colouredVisible++ }
                }
            }

            if ($name -eq 'outline.png') {
                if ($transparent -eq 0) {
                    throw "outline.png has no transparent pixels. Teams needs a transparent background with a white glyph - a fully opaque icon renders as a solid block in the app bar."
                }
                if ($colouredVisible -gt 0) {
                    Write-Warning "outline.png has $colouredVisible non-white visible pixel(s). Teams expects a white-only monochrome glyph."
                }
            }

            if ($distinct.Count -eq 1) {
                Write-Warning "$name is a single flat colour - looks like the placeholder swatch rather than real artwork."
            }
        }
        finally { $bitmap.Dispose() }
    }
}

Test-TeamsIcons -Root $scriptRoot

# Stage in a temp directory so the placeholder is never overwritten in the working tree.
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("dorc-teams-app-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null

try {
    $manifest = Get-Content -LiteralPath (Join-Path $scriptRoot 'manifest.json') -Raw
    if ($manifest -notmatch '__BOT_APP_ID__') {
        throw "manifest.json contains no __BOT_APP_ID__ placeholder - has it already been substituted?"
    }

    $manifest = $manifest.Replace('__BOT_APP_ID__', $appId)
    # -Encoding utf8 emits a BOM on Windows PowerShell, which the Teams catalog rejects.
    [System.IO.File]::WriteAllText(
        (Join-Path $staging 'manifest.json'),
        $manifest,
        (New-Object System.Text.UTF8Encoding($false)))

    Copy-Item -LiteralPath (Join-Path $scriptRoot 'color.png')   -Destination $staging
    Copy-Item -LiteralPath (Join-Path $scriptRoot 'outline.png') -Destination $staging

    if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }

    # Zip the files themselves, not the folder - Teams requires a flat archive.
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $OutputPath -Force

    Write-Host "Packaged $OutputPath for bot app id $appId."
    Write-Host "Upload via Teams admin center -> Teams apps -> Manage apps -> Upload new app."
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

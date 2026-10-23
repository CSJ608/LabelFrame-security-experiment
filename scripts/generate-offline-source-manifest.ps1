param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$InstallManifestPath,
    [Parameter(Mandatory = $true)][string]$ServerImageReference,
    [Parameter(Mandatory = $true)][string]$ServerImageDigest,
    [Parameter(Mandatory = $true)][string]$ComposePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$OwnerRepo = 'marci-labs/LabelFrame',
    [string]$InstallManifestUrl = '',
    [string]$ComposeUrl = '',
    [int]$SchemaVersion = 1,
    [string]$BuilderVersion = '1'
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-NormalizedTextSha256([string]$Path) {
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Fail([string]$Message) {
    Write-Host "::error::$Message"
    throw $Message
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') { Fail "版本号非法：$Version" }
if (-not (Test-Path -LiteralPath $InstallManifestPath -PathType Leaf)) { Fail "install manifest 不存在：$InstallManifestPath" }
if (-not (Test-Path -LiteralPath $ComposePath -PathType Leaf)) { Fail "compose 文件不存在：$ComposePath" }
if ($ServerImageReference -notmatch '^[^\s@]+:[^\s@]+$') { Fail "Server 镜像引用须包含固定 tag：$ServerImageReference" }
if ($ServerImageDigest -notmatch '^sha256:[0-9a-f]{64}$') { Fail "Server 镜像 digest 非法：$ServerImageDigest" }

try { $installManifest = Get-Content -LiteralPath $InstallManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { Fail "install manifest 不是合法 JSON：$($_.Exception.Message)" }
if ($installManifest.labelframeVersion -ne $Version) {
    Fail "install manifest 版本（$($installManifest.labelframeVersion)）与目标版本（$Version）不一致"
}

$releaseBase = "https://github.com/$OwnerRepo/releases/download/v$Version"
if (-not $InstallManifestUrl) { $InstallManifestUrl = "$releaseBase/install-manifest.json" }
if (-not $ComposeUrl) { $ComposeUrl = "$releaseBase/compose.yml" }
foreach ($url in @($InstallManifestUrl, $ComposeUrl, 'https://go.microsoft.com/fwlink/p/?LinkId=2124701')) {
    if (-not [Uri]::TryCreate($url, [UriKind]::Absolute, [ref]([Uri]$null)) -or -not $url.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) {
        Fail "外部来源必须是 HTTPS 绝对 URL：$url"
    }
}

$manifest = [ordered]@{
    schemaVersion = $SchemaVersion
    labelframeVersion = $Version
    installManifest = [ordered]@{
        url = $InstallManifestUrl
        sha256 = Get-Sha256 $InstallManifestPath
    }
    serverImage = [ordered]@{
        reference = $ServerImageReference
        digest = $ServerImageDigest
    }
    artifacts = @(
        [ordered]@{
            id = 'compose'
            fileName = 'compose.yml'
            url = $ComposeUrl
            sha256 = Get-NormalizedTextSha256 $ComposePath
        }
    )
    webView2Standalone = [ordered]@{
        url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124701'
        fileName = 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
        architecture = 'x64'
        publisher = 'Microsoft Corporation'
        minimumSizeBytes = 100000000
        verification = 'authenticode-publisher-file-name-minimum-size'
    }
    builder = [ordered]@{
        contractVersion = $BuilderVersion
        generator = 'generate-offline-source-manifest.ps1'
    }
}

$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$temp = "$OutputPath.tmp-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.File]::WriteAllText($temp, (($manifest | ConvertTo-Json -Depth 8) + "`n"), $utf8NoBom)
    Move-Item -LiteralPath $temp -Destination $OutputPath -Force
}
finally {
    Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
}
Write-Host "离线 Builder 来源清单已生成：$OutputPath"
exit 0

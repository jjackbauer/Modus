[CmdletBinding()]
param(
    [string]$TargetRepository = "",
    [string]$ControlRoot = "",
    [string]$DataRoot = "",
    [string]$AdapterBind = "127.0.0.1:5099",
    [string]$Bind = "127.0.0.1:3000",
    [string]$PublicOrigin = "",
    [ValidateSet("demo", "auto", "none")]
    [string]$Implementation = "demo",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Workspace = Split-Path -Parent $ScriptDir
$ControlPlane = Join-Path $Workspace "control-plane"
$DataHome = Join-Path $Workspace "control-plane-data"

if ([string]::IsNullOrWhiteSpace($TargetRepository)) { $TargetRepository = Join-Path $DataHome "demo-target" }
if ([string]::IsNullOrWhiteSpace($ControlRoot)) { $ControlRoot = Join-Path $DataHome "demo-control" }
if ([string]::IsNullOrWhiteSpace($DataRoot)) { $DataRoot = Join-Path $DataHome "demo-runtime" }

New-Item -ItemType Directory -Force $TargetRepository, $ControlRoot, $DataRoot, (Join-Path $DataHome "logs") | Out-Null

if (-not (Test-Path (Join-Path $TargetRepository ".git"))) {
    Write-Host "Initializing demo target repository at $TargetRepository"
    git init -b main $TargetRepository | Out-Null
    Set-Content -Path (Join-Path $TargetRepository "README.md") -Value "# Wip.Factory demo target" -Encoding utf8
    git -C $TargetRepository -c user.email="factory@example.test" -c user.name="Factory" add . | Out-Null
    git -C $TargetRepository -c user.email="factory@example.test" -c user.name="Factory" commit -m "chore: demo baseline" | Out-Null
}

$adapterProject = Join-Path $Workspace "Wip.Factory.ControlApi\Wip.Factory.ControlApi.csproj"
$adapterExe = Join-Path $Workspace "Wip.Factory.ControlApi\bin\Debug\net10.0\Wip.Factory.ControlApi.exe"

if (-not $SkipBuild) {
    Write-Host "Building .NET adapter..."
    dotnet build $adapterProject -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "adapter build failed" }

    Write-Host "Building Rust control plane (release)..."
    Push-Location $ControlPlane
    try {
        cargo build --release -p control-plane-server
        if ($LASTEXITCODE -ne 0) { throw "server build failed" }
        cargo build --release -p control-plane-web-client --target wasm32-unknown-unknown
        if ($LASTEXITCODE -ne 0) { throw "web client build failed" }
        wasm-bindgen --target web --no-typescript --out-dir server\static\pkg --out-name control_plane_web target\wasm32-unknown-unknown\release\control_plane_web_client.wasm
        if ($LASTEXITCODE -ne 0) { throw "wasm-bindgen failed" }
    }
    finally { Pop-Location }
}

if (-not (Test-Path $adapterExe)) { throw "adapter executable not found: $adapterExe" }

$token = $env:CONTROL_PLANE_ADAPTER_TOKEN
if ([string]::IsNullOrWhiteSpace($token) -or $token.Length -lt 16) {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $token = [Convert]::ToBase64String($bytes)
}
$env:CONTROL_PLANE_ADAPTER_TOKEN = $token

$adapterLog = Join-Path $DataHome "logs\adapter.out.log"
$adapterErr = Join-Path $DataHome "logs\adapter.err.log"
$serverLog = Join-Path $DataHome "logs\server.out.log"
$serverErr = Join-Path $DataHome "logs\server.err.log"

Write-Host "Starting adapter on $AdapterBind (implementation: $Implementation)..."
$adapter = Start-Process -FilePath $adapterExe -ArgumentList @(
    "--repo", "`"$TargetRepository`"",
    "--control-root", "`"$ControlRoot`"",
    "--token", "`"$token`"",
    "--implementation", $Implementation,
    "--bind", $AdapterBind
) -WorkingDirectory $Workspace -PassThru -RedirectStandardOutput $adapterLog -RedirectStandardError $adapterErr
$adapter.Id | Set-Content (Join-Path $DataRoot "adapter.pid")
Start-Sleep -Seconds 2

$env:CONTROL_PLANE_ADAPTER_URL = "http://$AdapterBind"
$env:CONTROL_PLANE_DATA = $DataRoot
$env:CONTROL_PLANE_BIND = $Bind
$env:CONTROL_PLANE_LOG = "info"
if (-not [string]::IsNullOrWhiteSpace($PublicOrigin)) { $env:CONTROL_PLANE_PUBLIC_ORIGIN = $PublicOrigin }

Write-Host "Starting control plane on http://$Bind ..."
$serverExe = Join-Path $ControlPlane "target\release\control-plane-server.exe"
$server = Start-Process -FilePath $serverExe -WorkingDirectory $ControlPlane -PassThru -RedirectStandardOutput $serverLog -RedirectStandardError $serverErr
$server.Id | Set-Content (Join-Path $DataRoot "server.pid")

$deadline = (Get-Date).AddSeconds(30)
$ready = $false
do {
    Start-Sleep -Milliseconds 500
    try {
        $response = Invoke-WebRequest -Uri "http://$Bind/login" -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
} while ((Get-Date) -lt $deadline)

if (-not $ready) { throw "control plane did not become ready; inspect $serverErr" }

Write-Host ""
Write-Host "Control plane: http://$Bind"
Write-Host "Adapter:       http://$AdapterBind (service token is held only by the server)"
Write-Host "Data:          $DataRoot"
Write-Host "Logs:          $DataHome\logs"
$bootstrap = Join-Path $DataRoot "bootstrap-admin.txt"
if (Test-Path $bootstrap) { Write-Host "First-admin password: $bootstrap" }
Write-Host "Stop with: powershell -File tools/stop-control-plane.ps1 -DataRoot `"$DataRoot`""
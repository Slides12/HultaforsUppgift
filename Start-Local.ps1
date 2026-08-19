[CmdletBinding()]
param(
    [ValidateRange(1, 16)]
    [int]$WorkerCount = 4
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$functionProject = Join-Path $repoRoot 'src\IntegrationAssignment'
$receiverProject = Join-Path $repoRoot 'src\MockProductReceiver'
$localSettingsPath = Join-Path $functionProject 'local.settings.json'
$azuriteDataPath = Join-Path $repoRoot '.azurite'
$npmCommandPath = Join-Path $env:APPDATA 'npm'
$functionPorts = @(7071)

if ($WorkerCount -gt 1) {
    $functionPorts += 7073..(7071 + $WorkerCount)
}

if (Test-Path -LiteralPath $npmCommandPath) {
    $env:PATH = "$npmCommandPath;$env:PATH"
}

function Assert-CommandExists {
    param([Parameter(Mandatory)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. Follow the prerequisite instructions in README.md."
    }
}

function Test-ListeningPort {
    param([Parameter(Mandatory)][int]$Port)

    return $null -ne (Get-NetTCPConnection `
        -LocalPort $Port `
        -State Listen `
        -ErrorAction SilentlyContinue `
        | Select-Object -First 1)
}

Assert-CommandExists 'dotnet'
Assert-CommandExists 'func'
Assert-CommandExists 'azurite'

if (-not (Test-Path -LiteralPath $localSettingsPath)) {
    throw "Missing $localSettingsPath. Copy local.settings.example.json to local.settings.json and add the local RabbitMQ values first."
}

$settings = Get-Content -LiteralPath $localSettingsPath -Raw | ConvertFrom-Json
$rabbitHost = $settings.Values.'RabbitMQ__Host'

if ([string]::IsNullOrWhiteSpace($rabbitHost)) {
    throw 'RabbitMQ__Host is missing from local.settings.json.'
}

if (Test-ListeningPort -Port 10000) {
    Write-Host 'Azurite is already running.' -ForegroundColor Yellow
}
else {
    Write-Host 'Starting Azurite in a new window...'
    $azuriteCommand = "`$Host.UI.RawUI.WindowTitle = 'Azurite'; Set-Location -LiteralPath '$repoRoot'; azurite --silent --location '$azuriteDataPath'"
    Start-Process `
        -FilePath 'powershell.exe' `
        -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-Command', $azuriteCommand `
        -WorkingDirectory $repoRoot
}

if (Test-ListeningPort -Port 7072) {
    Write-Host 'The mock receiving system is already running.' -ForegroundColor Yellow
}
else {
    Write-Host 'Starting the mock receiving system in a new window...'
    $receiverCommand = "`$Host.UI.RawUI.WindowTitle = 'Mock Receiving System - JSON to XML'; Set-Location -LiteralPath '$receiverProject'; dotnet run"
    Start-Process `
        -FilePath 'powershell.exe' `
        -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-Command', $receiverCommand `
        -WorkingDirectory $receiverProject
}

for ($workerIndex = 0; $workerIndex -lt $WorkerCount; $workerIndex++) {
    $workerNumber = $workerIndex + 1
    $functionPort = $functionPorts[$workerIndex]

    if (Test-ListeningPort -Port $functionPort) {
        Write-Host "Functions worker $workerNumber is already running on port $functionPort." -ForegroundColor Yellow
        continue
    }

    Write-Host "Starting Functions worker $workerNumber of $WorkerCount on port $functionPort in a new window..."
    $functionsCommand = "`$Host.UI.RawUI.WindowTitle = 'Azure Functions Worker $workerNumber of $WorkerCount - Port $functionPort'; Set-Location -LiteralPath '$functionProject'; dotnet run --no-build -- --port $functionPort"
    Start-Process `
        -FilePath 'powershell.exe' `
        -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-Command', $functionsCommand `
        -WorkingDirectory $functionProject
}

$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Milliseconds 500
    $azuriteReady = Test-ListeningPort -Port 10000
    $functionWorkersReady = @($functionPorts | Where-Object { Test-ListeningPort -Port $_ }).Count
    $receiverReady = Test-ListeningPort -Port 7072
} until (($azuriteReady -and $functionWorkersReady -eq $WorkerCount -and $receiverReady) -or (Get-Date) -gt $deadline)

if (-not $azuriteReady) {
    throw 'Azurite did not start listening on port 10000 within 30 seconds. Check the Azurite window.'
}

if ($functionWorkersReady -ne $WorkerCount) {
    throw "Only $functionWorkersReady of $WorkerCount Functions workers started listening within 30 seconds. Check the Functions windows."
}

if (-not $receiverReady) {
    throw 'The mock receiving system did not start listening on port 7072 within 30 seconds. Check the receiver window.'
}

Write-Host ''
Write-Host 'Everything is ready.' -ForegroundColor Green
Write-Host 'Product endpoint:     http://localhost:7071/api/products'
Write-Host "Functions workers:    $WorkerCount (parallel RabbitMQ consumers)"
Write-Host 'Mock receiver:        http://localhost:7072/products'
Write-Host 'Receiver health:      http://localhost:7072/health'
Write-Host "RabbitMQ Management:  http://${rabbitHost}:15672"

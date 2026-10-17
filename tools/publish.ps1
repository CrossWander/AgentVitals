<#
    Собирает готовый к запуску exe в папку release\.

    По умолчанию — один файл, которому нужен установленный .NET 10 Runtime
    (https://dotnet.microsoft.com/download/dotnet/10.0).
    -SelfContained — рантайм внутри, ставить ничего не нужно, но файл в несколько раз больше.
    -Version — версия exe, например тег релиза v1.0.0; без него — из AgentVitals.csproj.
#>
param(
    [switch]$SelfContained,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'release'

# Дожидаемся выхода: без этого exe ещё занят и удалить его не выйдет.
Get-Process AgentVitals -ErrorAction SilentlyContinue |
    Stop-Process -Force -PassThru |
    Wait-Process -Timeout 10 -ErrorAction SilentlyContinue

# Настройки, журнал и состояния сессий лежат рядом с exe и пересборку переживают.
$keep = 'config.json', 'log.txt', 'sessions'
if (Test-Path $output) {
    $stale = { Get-ChildItem $output -Force | Where-Object { $_.Name -notin $keep } }

    # Одиночный exe ещё секунду занят после выхода: антивирус успевает открыть его на чтение.
    for ($try = 1; $try -le 10 -and (& $stale); $try++) {
        & $stale | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        if (& $stale) {
            Start-Sleep -Milliseconds 500
        }
    }

    if (& $stale) {
        throw "не удалось очистить $output - файл занят другим процессом"
    }
}

$arguments = @(
    'publish', (Join-Path $root 'src\AgentVitals\AgentVitals.csproj')
    '-c', 'Release'
    '-r', 'win-x64'
    '-o', $output
    '--nologo'
    '-p:PublishSingleFile=true'
    '-p:DebugType=none'
    # Skia и HarfBuzz — нативные библиотеки Avalonia; без этого они лягут рядом отдельными файлами.
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' })
)

if ($SelfContained) {
    $arguments += '-p:EnableCompressionInSingleFile=true'
}

if ($Version) {
    $arguments += "-p:Version=$($Version.TrimStart('v'))"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish завершился с кодом $LASTEXITCODE"
}

# Пакеты Skia и HarfBuzz кладут рядом свои нативные .pdb на сотню мегабайт — для запуска не нужны.
Get-ChildItem $output -Filter *.pdb | Remove-Item -Force

$exe = Get-Item (Join-Path $output 'AgentVitals.exe')
Write-Host ''
Write-Host "$($exe.FullName) — $([Math]::Round($exe.Length / 1MB, 1)) МБ"

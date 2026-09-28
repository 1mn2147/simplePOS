param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"
$localDotnet = Join-Path $root ".dotnet-sdk\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $dotnetCommand.Source
}
$output = Join-Path $root "artifacts\publish\$Runtime"
$publishRoot = [IO.Path]::GetFullPath((Join-Path $root "artifacts\publish"))
$resolvedOutput = [IO.Path]::GetFullPath($output)
if (-not $resolvedOutput.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "게시 출력 경로가 프로젝트의 artifacts/publish 밖에 있습니다."
}
if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

& $dotnet publish (Join-Path $root "src\Pos.App\Pos.App.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $output

$exe = Join-Path $output "SimplePOS.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "게시 결과에서 SimplePOS.exe를 찾지 못했습니다."
}

$hash = Get-FileHash -LiteralPath $exe -Algorithm SHA256
$hash.Hash + "  " + (Split-Path -Leaf $exe) | Set-Content -LiteralPath "$exe.sha256" -Encoding ascii
Write-Host "게시 완료: $exe"

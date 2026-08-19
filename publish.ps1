$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Get-DotnetRid {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $cpu = switch ($arch) {
        ([System.Runtime.InteropServices.Architecture]::X64) { "x64" }
        ([System.Runtime.InteropServices.Architecture]::Arm64) { "arm64" }
        ([System.Runtime.InteropServices.Architecture]::X86) { "x86" }
        ([System.Runtime.InteropServices.Architecture]::Arm) { "arm" }
        default { throw "Unsupported CPU architecture: $arch" }
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return "win-$cpu"
    }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        return "osx-$cpu"
    }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        return "linux-$cpu"
    }

    throw "Unsupported OS"
}

$rid = Get-DotnetRid
$exe = if ($rid.StartsWith("win-")) { "ats.exe" } else { "ats" }
$out = Join-Path "artifacts" $rid

Push-Location src/AgentTokenStats.Web
if (Test-Path package-lock.json) { npm ci } else { npm install }
npm run build
Pop-Location

dotnet publish src/AgentTokenStats/AgentTokenStats.csproj `
    -c Release `
    -r $rid `
    --self-contained true `
    -o $out

if (-not $rid.StartsWith("win-") -and (Get-Command chmod -ErrorAction SilentlyContinue)) {
    & chmod +x (Join-Path $out $exe)
}

Write-Host "Published to $out/$exe"

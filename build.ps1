[CmdletBinding()]
param(
    [string]$OutDir,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
if (!$OutDir) { $OutDir = Join-Path $PSScriptRoot 'build' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
$src = Join-Path $PSScriptRoot 'src'
$tests = Join-Path $PSScriptRoot 'tests'
$framework = if ([Environment]::Is64BitOperatingSystem) { 'Framework64' } else { 'Framework' }
$compiler = Join-Path $env:WINDIR "Microsoft.NET\$framework\v4.0.30319\csc.exe"
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework 4.x C# compiler was not found.'
}

[void][IO.Directory]::CreateDirectory($OutDir)
$tempDir = Join-Path $OutDir 'temp'
[void][IO.Directory]::CreateDirectory($tempDir)
$oldTemp, $oldTmp = $env:TEMP, $env:TMP
$env:TEMP = $env:TMP = $tempDir
try {
    $common = @('/nologo', '/platform:anycpu', '/optimize+', '/langversion:5',
        '/codepage:65001', '/warn:4', '/warnaserror+', '/reference:System.dll',
        '/reference:System.Core.dll', '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll', "/win32manifest:$src\app.manifest")

    function Build-Executable([string]$name, [string]$target, [string[]]$files) {
        $extra = @()
        if ($target -eq 'winexe') { $extra += "/win32icon:$src\Locator.ico" }
        & $compiler @common "/target:$target" "/out:$OutDir\$name" @extra @files
        if ($LASTEXITCODE -ne 0) { throw "Compilation failed for $name." }
    }

    $app = 'EvaMouseLocator.exe'
    Build-Executable $app 'winexe' @("$src\Program.cs", "$src\MouseShake.cs", "$src\BurstOverlay.cs")
    Write-Host "Built $(Join-Path $OutDir $app)"

    if ($Test) {
        $suites = [ordered]@{
            'GestureTests.exe' = @("$src\MouseShake.cs", "$tests\GestureTests.cs")
            'FrameTests.exe' = @("$src\BurstOverlay.cs", "$tests\FrameTests.cs")
            'DesktopTests.exe' = @("$src\BurstOverlay.cs", "$tests\DesktopTests.cs")
        }
        foreach ($suite in $suites.Keys) {
            Build-Executable $suite 'exe' $suites[$suite]
            & (Join-Path $OutDir $suite)
            if ($LASTEXITCODE -ne 0) { throw "$suite failed with exit code $LASTEXITCODE." }
        }
    }
}
finally {
    $env:TEMP, $env:TMP = $oldTemp, $oldTmp
}

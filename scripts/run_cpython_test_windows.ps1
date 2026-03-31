#!/usr/bin/env pwsh
<#
.SYNOPSIS
Run Windows OS tests through Naja compiler - Native Naja version.

.DESCRIPTION
Compiles test_windows.naja using Naja and runs the test suite,
testing Naja's Windows-specific os module functionality.

.EXAMPLE
.\run_cpython_test_windows.ps1

.NOTES
- Compiles pure Naja syntax (.naja file)
- Tests Windows-specific os module functionality
- Generates detailed test results
#>

param(
    [string]$TestFile = "Naja.CodeGen.Tests\testdata\windows_os\test_windows.naja",
    [string]$OutputDir = "test_results",
    [switch]$Verbose = $false
)

# Ensure output directory exists
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
    Write-Host "Created output directory: $OutputDir" -ForegroundColor Green
}

# Check if test file exists
if (!(Test-Path $TestFile)) {
    Write-Host "ERROR: Test file not found: $TestFile" -ForegroundColor Red
    exit 1
}

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Naja Windows OS Tests - test_windows.naja" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Get absolute path
$TestFilePath = (Resolve-Path $TestFile).Path
Write-Host "Test File: $TestFilePath" -ForegroundColor Yellow
Write-Host "Output Dir: $(Resolve-Path $OutputDir)" -ForegroundColor Yellow
Write-Host "Language: Pure Naja (.naja)" -ForegroundColor Yellow
Write-Host ""

# Compile using Naja
Write-Host "Compiling with Naja compiler..." -ForegroundColor Cyan
$CompilerCmd = "dotnet run --project Naja.CLI -- compile `"$TestFilePath`""

if ($Verbose) {
    Write-Host "Command: $CompilerCmd" -ForegroundColor Gray
}

$CompileOutput = Invoke-Expression $CompilerCmd 2>&1
$CompileSuccess = $LASTEXITCODE -eq 0

if ($CompileSuccess) {
    Write-Host "✓ Compilation successful" -ForegroundColor Green
} else {
    Write-Host "✗ Compilation failed" -ForegroundColor Red
    Write-Host "Error output:" -ForegroundColor Yellow
    Write-Host $CompileOutput -ForegroundColor Red
    exit 1
}

Write-Host ""

# Find the compiled assembly
$BaseName = [System.IO.Path]::GetFileNameWithoutExtension($TestFile)
$AssemblyPath = "$BaseName.dll"

if (!(Test-Path $AssemblyPath)) {
    Write-Host "WARNING: Could not find compiled assembly: $AssemblyPath" -ForegroundColor Yellow
    Write-Host "Compiler output:" -ForegroundColor Gray
    Write-Host $CompileOutput
    exit 1
}

Write-Host "Running compiled test assembly: $(Resolve-Path $AssemblyPath)" -ForegroundColor Cyan
Write-Host ""

# Execute the compiled assembly
try {
    $TestOutput = & $AssemblyPath
    Write-Host $TestOutput

    # Save results to file
    $ResultsFile = Join-Path $OutputDir "test_results_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"
    $TestOutput | Out-File -FilePath $ResultsFile -Encoding UTF8
    Write-Host ""
    Write-Host "Results saved to: $ResultsFile" -ForegroundColor Green

    Write-Host ""
    Write-Host "✓ Tests executed successfully" -ForegroundColor Green
}
catch {
    Write-Host "✗ Test execution failed: $_" -ForegroundColor Red
    exit 1
}


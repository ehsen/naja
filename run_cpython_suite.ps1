# CPython Test Suite Runner - Baseline Report
# This script runs all CPython tests and reports failures

$cpythonRoot = $env:CPYTHON_REPO
if (-not $cpythonRoot) {
    Write-Error "CPYTHON_REPO environment variable not set"
    exit 1
}

$testDir = "$cpythonRoot/Lib/test"
if (-not (Test-Path $testDir)) {
    Write-Error "CPython test directory not found: $testDir"
    exit 1
}

# Run only the bulk test to get overall baseline
Write-Host "Running CPython bulk suite test..." -ForegroundColor Cyan
dotnet test --project "Naja.CodeGen.Tests/Naja.CodeGen.Tests.csproj" --filter "Name=CPython_BulkSuite_PassRate" --no-build -v minimal

Write-Host "Bulk suite test complete!" -ForegroundColor Green

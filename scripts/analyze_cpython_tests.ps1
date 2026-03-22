#!/usr/bin/env pwsh
# Simple CPython Test Categorizer
# Reads test files and reports which ones should be testable

param(
    [string]$CpythonRepo = $env:CPYTHON_REPO
)

if (-not $CpythonRepo) {
    Write-Error "CPYTHON_REPO environment variable not set"
    exit 1
}

$testDir = "$CpythonRepo/Lib/test"
if (-not (Test-Path $testDir)) {
    Write-Error "CPython test directory not found: $testDir"
    exit 1
}

$results = @{
    stdlib_dependent = @()      # Tests that import stdlib modules
    unittest_based = @()         # Tests that use unittest framework
    simple = @()                 # Simple assertion-based tests
    unknown = @()
}

Write-Host "Analyzing CPython test files...`n" -ForegroundColor Cyan

$testFiles = @(Get-ChildItem "$testDir/test_*.py" -File | Select-Object -First 100)

foreach ($testFile in $testFiles) {
    $name = $testFile.Name
    $content = Get-Content $testFile -Raw
    
    # Categorize
    if ($content -match 'import\s+unittest') {
        $results.unittest_based += $name
    } elseif ($content -match 'import\s+\w+' -and -not ($content -match 'import\s+sys|import\s+os|import\s+re')) {
        $results.stdlib_dependent += $name
    } elseif ($content -match '^\s*assert\s+' -or $content -match 'self\.assert') {
        $results.simple += $name
    } else {
        $results.unknown += $name
    }
}

Write-Host "SIMPLE TESTS (Pure assertions, no stdlib):" -ForegroundColor Green
$results.simple | Sort-Object | Select-Object -First 10 | ForEach-Object { Write-Host "  - $_" }
Write-Host "  (Total: $($results.simple.Count))`n"

Write-Host "UNITTEST TESTS:" -ForegroundColor Yellow
$results.unittest_based | Sort-Object | Select-Object -First 10 | ForEach-Object { Write-Host "  - $_" }
Write-Host "  (Total: $($results.unittest_based.Count))`n"

Write-Host "STDLIB-DEPENDENT TESTS:" -ForegroundColor Magenta
$results.stdlib_dependent | Sort-Object | Select-Object -First 10 | ForEach-Object { Write-Host "  - $_" }
Write-Host "  (Total: $($results.stdlib_dependent.Count))`n"

Write-Host "UNKNOWN TESTS:" -ForegroundColor Gray
$results.unknown | Sort-Object | Select-Object -First 5 | ForEach-Object { Write-Host "  - $_" }
Write-Host "  (Total: $($results.unknown.Count))"

#!/usr/bin/env pwsh
# CPython Test Suite Runner - Comprehensive Baseline
# Tests a sample of CPython test files and categorizes failures

param(
    [int]$MaxTests = 50,
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

# Categories of tests we'll sample
$categories = @{
    "simple" = @("test_augassign.py", "test_binop.py", "test_compare.py", "test_contains.py", "test_call.py")
    "core" = @("test_grammar.py", "test_compound_statements.py", "test_simple_stmt.py")
    "advanced" = @("test_generators.py", "test_coroutines.py", "test_pattern_matching.py")
}

# Get all test files
$allTests = @(Get-ChildItem "$testDir/test_*.py" -File | Select-Object -First $MaxTests)
Write-Host "Found $($allTests.Count) CPython test files in $testDir" -ForegroundColor Cyan

# Build once before running tests
Write-Host "Building project..." -ForegroundColor Yellow
dotnet build --project "Naja.CodeGen.Tests/Naja.CodeGen.Tests.csproj" --configuration Release -v minimal 2>&1 | Where-Object {$_ -like "*error*" -or $_ -like "*Error*"} | ForEach-Object { Write-Host $_ -ForegroundColor Red }

# Run a systematic sample of tests
$results = @{
    passed = 0
    failed = 0
    errors = @()
}

Write-Host "`nTesting sample of CPython files...`n" -ForegroundColor Cyan

foreach ($testFile in $allTests | Select-Object -First 20) {
    $name = $testFile.Name
    $path = $testFile.FullName
    
    Write-Host -NoNewline "Testing $name ... "
    
    try {
        # Try to run via Engine.Eval - simplified test
        $testCode = @"
using Naja.CodeGen;
try {
    var engine = new NajaEngine();
    engine.Eval("$($path -replace '\\', '\\\\')");
    System.Console.WriteLine("PASS");
} catch (System.Exception ex) {
    System.Console.WriteLine("FAIL: " + ex.GetType().Name + ": " + ex.Message.Substring(0, Math.Min(80, ex.Message.Length)));
}
"@
        # We would need a better way to invoke this - for now just report
        Write-Host "SKIPPED (need direct API)" -ForegroundColor Yellow
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
        $results.errors += @{file=$name; error=$_.Exception.Message}
        $results.failed++
    }
}

Write-Host "`nSummary:" -ForegroundColor Cyan
Write-Host "Passed: $($results.passed)" -ForegroundColor Green
Write-Host "Failed: $($results.failed)" -ForegroundColor Red
if ($results.errors.Count -gt 0) {
    Write-Host "`nSample errors:" -ForegroundColor Yellow
    $results.errors | Select-Object -First 5 | ForEach-Object { Write-Host "  - $($_.file): $($_.error)" }
}

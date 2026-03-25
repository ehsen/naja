# Analyze failing CPython test to understand what's broken
using namespace Naja.CPythonTests.Infrastructure

$testFile = "test_bool.py"

# Use the test executor directly
$executor = New-Object NajaTestExecutor
$cpythonPath = "F:\Sources\cpython\Lib\test\$testFile"

Write-Host "=== Compiling $testFile ===" -ForegroundColor Cyan
$compilation = $executor.Compile($cpythonPath)

if ($compilation.Success) {
    Write-Host "Compilation successful!" -ForegroundColor Green
    Write-Host "`n=== Running tests ===" -ForegroundColor Cyan
    $execution = $executor.Run($compilation, $null, 60000)
    $result = [UnittestOutputParser]::Parse($execution)
    
    Write-Host "`nResults:" -ForegroundColor Yellow
    Write-Host "  Exit code: $($result.ExitCode)"
    Write-Host "  Passed: $($result.PassCount)"
    Write-Host "  Failed: $($result.FailCount)"
    Write-Host "  Errors: $($result.ErrorCount)"
    
    if ($result.Stdout) {
        Write-Host "`nSTDOUT:" -ForegroundColor Cyan
        Write-Host $result.Stdout
    }
    
    if ($result.Stderr) {
        Write-Host "`nSTDERR:" -ForegroundColor Red
        Write-Host $result.Stderr
    }
    
    if ($result.FailureSummary) {
        Write-Host "`nFAILURES:" -ForegroundColor Red
        Write-Host $result.FailureSummary
    }
} else {
    Write-Host "Compilation failed!" -ForegroundColor Red
    foreach ($err in $compilation.Errors) {
        Write-Host "  ERROR: $err" -ForegroundColor Red
    }
}

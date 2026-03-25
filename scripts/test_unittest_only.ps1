# Find CPython unittest-only tests and attempt to compile them with Naja
# This discovers which tests are ready to run vs. which need work

$cpythonTestPath = "F:\Sources\cpython\Lib\test"
$najaTestExecutor = "Naja.CPythonTests\bin\Debug\net10.0-windows\Naja.CPythonTests.dll"

# Import the test infrastructure
Add-Type -Path "Naja.CPythonTests\bin\Debug\net10.0-windows\Naja.CPythonTests.dll"
Add-Type -Path "Naja.CLI\bin\Debug\net10.0-windows\Naja.CodeGen.dll"
Add-Type -Path "Naja.CLI\bin\Debug\net10.0-windows\Naja.Parser.dll"
Add-Type -Path "Naja.CLI\bin\Debug\net10.0-windows\Naja.Lexer.dll"
Add-Type -Path "Naja.CLI\bin\Debug\net10.0-windows\Naja.Semantics.dll"

$executor = New-Object Naja.CPythonTests.Infrastructure.NajaTestExecutor

# Get list of unittest-only test files
$unittestOnlyTests = @(
    "test_bool.py",
    "test_bytes.py",
    "test_class.py",
    "test_complex.py",
    "test_dict.py",
    "test_float.py",
    "test_int.py",
    "test_list.py",
    "test_scope.py",
    "test_slice.py",
    "test_str.py",
    "test_tuple.py",
    "test_type.py"
)

$results = @()

Write-Host "Testing $($unittestOnlyTests.Count) unittest-only CPython tests..." -ForegroundColor Cyan
Write-Host ""

foreach ($testFile in $unittestOnlyTests) {
    $fullPath = Join-Path $cpythonTestPath $testFile
    
    if (-not (Test-Path $fullPath)) {
        Write-Host "  [$testFile] SKIP - file not found" -ForegroundColor Gray
        continue
    }
    
    try {
        Write-Host "  [$testFile] Compiling..." -NoNewline
        $compilation = $executor.Compile($fullPath)
        
        if ($compilation.Success) {
            Write-Host " OK" -ForegroundColor Green -NoNewline
            
            # Try to run it
            Write-Host " | Running..." -NoNewline
            try {
                $execution = $executor.Run($compilation, $null, 30000)
                $result = [Naja.CPythonTests.Infrastructure.UnittestOutputParser]::Parse($execution)
                
                if ($result.Passed) {
                    Write-Host " PASS ($($result.PassCount) tests)" -ForegroundColor Green
                    $results += [PSCustomObject]@{
                        File = $testFile
                        Status = "PASS"
                        PassCount = $result.PassCount
                        FailCount = $result.FailCount
                        ErrorCount = $result.ErrorCount
                        Issue = ""
                    }
                } else {
                    Write-Host " FAIL ($($result.PassCount) pass, $($result.FailCount) fail, $($result.ErrorCount) error)" -ForegroundColor Red
                    $issue = if ($result.FailureSummary) { 
                        ($result.FailureSummary -split "`n" | Select-Object -First 1).Substring(0, [Math]::Min(80, $result.FailureSummary.Length))
                    } else { "Runtime failure" }
                    $results += [PSCustomObject]@{
                        File = $testFile
                        Status = "RUN_FAIL"
                        PassCount = $result.PassCount
                        FailCount = $result.FailCount
                        ErrorCount = $result.ErrorCount
                        Issue = $issue
                    }
                }
            } catch {
                Write-Host " RUN_ERROR" -ForegroundColor Red
                $results += [PSCustomObject]@{
                    File = $testFile
                    Status = "RUN_ERROR"
                    PassCount = 0
                    FailCount = 0
                    ErrorCount = 0
                    Issue = $_.Exception.Message.Substring(0, [Math]::Min(80, $_.Exception.Message.Length))
                }
            }
        } else {
            $firstError = ($compilation.Errors | Select-Object -First 1)
            $shortError = if ($firstError.Length -gt 60) { $firstError.Substring(0, 60) + "..." } else { $firstError }
            Write-Host " COMPILE_FAIL: $shortError" -ForegroundColor Yellow
            $results += [PSCustomObject]@{
                File = $testFile
                Status = "COMPILE_FAIL"
                PassCount = 0
                FailCount = 0
                ErrorCount = 0
                Issue = $shortError
            }
        }
    } catch {
        Write-Host " ERROR: $($_.Exception.Message)" -ForegroundColor Red
        $results += [PSCustomObject]@{
            File = $testFile
            Status = "ERROR"
            PassCount = 0
            FailCount = 0
            ErrorCount = 0
            Issue = $_.Exception.Message.Substring(0, [Math]::Min(80, $_.Exception.Message.Length))
        }
    }
}

Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
$passing = $results | Where-Object { $_.Status -eq "PASS" }
$runFail = $results | Where-Object { $_.Status -eq "RUN_FAIL" }
$compileFail = $results | Where-Object { $_.Status -eq "COMPILE_FAIL" }

Write-Host "PASSING: $($passing.Count)" -ForegroundColor Green
if ($passing.Count -gt 0) {
    $passing | Format-Table File, PassCount -AutoSize
}

Write-Host "`nCOMPILE FAILURES: $($compileFail.Count)" -ForegroundColor Yellow
if ($compileFail.Count -gt 0) {
    $compileFail | Format-Table File, Issue -AutoSize -Wrap
}

Write-Host "`nRUNTIME FAILURES: $($runFail.Count)" -ForegroundColor Red
if ($runFail.Count -gt 0) {
    $runFail | Format-Table File, PassCount, FailCount, ErrorCount, Issue -AutoSize -Wrap
}

$totalPassRate = if ($results.Count -gt 0) { [math]::Round(($passing.Count / $results.Count) * 100, 1) } else { 0 }
Write-Host "`nOVERALL: $($passing.Count)/$($results.Count) ($totalPassRate%) unittest-only tests passing" -ForegroundColor Cyan

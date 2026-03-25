# Quick test runner for a specific CPython test file
$testFile = "test_bool.py"
$cpythonPath = "F:\Sources\cpython\Lib\test\$testFile"
$tempFile = "temp_test_runner.py"
$najaExe = "Naja.CLI\bin\Debug\net10.0-windows\naja.exe"

# Read the test file content
$content = Get-Content $cpythonPath -Raw

# Save to temp location
Set-Content $tempFile $content

# Compile and run using Naja
Write-Host "=== Compiling $testFile ===" -ForegroundColor Cyan
& $najaExe compile $tempFile -o "temp_test.dll"

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n=== Running compiled test ===" -ForegroundColor Green
    dotnet "temp_test.dll"
    Write-Host "`nExit code: $LASTEXITCODE" -ForegroundColor $(if ($LASTEXITCODE -eq 0) { 'Green' } else { 'Red' })
} else {
    Write-Host "`nCompilation failed with exit code $LASTEXITCODE" -ForegroundColor Red
}

# Cleanup
if (Test-Path $tempFile) { Remove-Item $tempFile }
if (Test-Path "temp_test.dll") { Remove-Item "temp_test.dll" }
if (Test-Path "temp_test.pdb") { Remove-Item "temp_test.pdb" }

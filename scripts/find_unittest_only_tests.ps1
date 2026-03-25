# Find CPython tests that only depend on unittest (no complex stdlib dependencies)
$cpythonTestPath = "F:\Sources\cpython\Lib\test"
$results = @()

Get-ChildItem -Path $cpythonTestPath -Filter "test_*.py" | ForEach-Object {
    $fileName = $_.Name
    $content = Get-Content $_.FullName -Raw
    
    # Extract all imports
    $imports = [regex]::Matches($content, '^\s*(?:from|import)\s+([a-zA-Z_][a-zA-Z0-9_.]*)') | 
        ForEach-Object { $_.Groups[1].Value } |
        Where-Object { $_ -ne 'test' -and $_ -ne 'unittest' -and $_ -ne '__future__' -and $_ -ne 'sys' } |
        Select-Object -Unique
    
    # Check if it imports unittest
    $hasUnittest = $content -match '(?:^|\s)import\s+unittest|from\s+unittest'
    
    # Count imports (excluding test, unittest, __future__, sys which are minimal)
    $complexImports = $imports | Where-Object { 
        $_ -notin @('os', 're', 'io', 'gc', 'weakref', 'collections', 'itertools', 'functools')
    }
    
    $results += [PSCustomObject]@{
        FileName = $fileName
        HasUnittest = $hasUnittest
        ImportCount = $imports.Count
        ComplexImportCount = $complexImports.Count
        Imports = ($imports -join ', ')
        ComplexImports = ($complexImports -join ', ')
    }
}

# Filter for unittest-only tests (or simple stdlib)
Write-Host "`n=== Tests with ONLY unittest (0 complex imports) ===" -ForegroundColor Green
$results | Where-Object { $_.HasUnittest -and $_.ComplexImportCount -eq 0 } | 
    Sort-Object ImportCount, FileName |
    Format-Table FileName, ImportCount, Imports -AutoSize

Write-Host "`n=== Tests with unittest + 1-2 simple imports ===" -ForegroundColor Yellow
$results | Where-Object { $_.HasUnittest -and $_.ComplexImportCount -gt 0 -and $_.ComplexImportCount -le 2 } |
    Sort-Object ComplexImportCount, ImportCount, FileName |
    Format-Table FileName, ComplexImportCount, ComplexImports -AutoSize -Wrap

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
$unittestOnly = ($results | Where-Object { $_.HasUnittest -and $_.ComplexImportCount -eq 0 }).Count
$simple = ($results | Where-Object { $_.HasUnittest -and $_.ComplexImportCount -le 2 -and $_.ComplexImportCount -gt 0 }).Count
$hasUnittest = ($results | Where-Object { $_.HasUnittest }).Count
$total = $results.Count

Write-Host "Total test files: $total"
Write-Host "Files with unittest: $hasUnittest"
Write-Host "Unittest-only (0 complex imports): $unittestOnly"
Write-Host "Unittest + 1-2 simple imports: $simple"

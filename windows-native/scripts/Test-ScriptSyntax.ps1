# PowerShell Syntax Validation Test
# This script checks for common syntax issues

$scriptPath = Join-Path $PSScriptRoot "Setup-Dependencies-Enhanced.ps1"

Write-Host "Validating PowerShell script syntax..." -ForegroundColor Cyan
Write-Host "Script: $scriptPath" -ForegroundColor Cyan
Write-Host ""

# Test 1: Check if file exists
if (-not (Test-Path $scriptPath)) {
    Write-Host "ERROR: Script file not found!" -ForegroundColor Red
    exit 1
}

# Test 2: Try to parse the script
try {
    $null = [System.Management.Automation.PSParser]::Tokenize((Get-Content $scriptPath -Raw), [ref]$null)
    Write-Host "✓ PowerShell tokenization successful" -ForegroundColor Green
}
catch {
    Write-Host "✗ Tokenization failed: $_" -ForegroundColor Red
    exit 1
}

# Test 3: Check for common issues
$content = Get-Content $scriptPath -Raw

# Check for unmatched quotes
$singleQuotes = ([regex]::Matches($content, "(?<!\\)'")).Count
$doubleQuotes = ([regex]::Matches($content, '(?<!\\)"')).Count

if ($singleQuotes % 2 -ne 0) {
    Write-Host "⚠ Warning: Unmatched single quotes detected" -ForegroundColor Yellow
}
else {
    Write-Host "✓ Single quotes balanced" -ForegroundColor Green
}

if ($doubleQuotes % 2 -ne 0) {
    Write-Host "⚠ Warning: Unmatched double quotes detected" -ForegroundColor Yellow
}
else {
    Write-Host "✓ Double quotes balanced" -ForegroundColor Green
}

# Test 4: Check for proper here-string format
$hereStringStart = ([regex]::Matches($content, '@"')).Count
$hereStringEnd = ([regex]::Matches($content, '"@')).Count

if ($hereStringStart -eq $hereStringEnd) {
    Write-Host "✓ Here-strings balanced ($hereStringStart pairs)" -ForegroundColor Green
}
else {
    Write-Host "✗ Unbalanced here-strings: $hereStringStart starts, $hereStringEnd ends" -ForegroundColor Red
}

# Test 5: Try to load without executing
try {
    $errors = $null
    $null = [System.Management.Automation.PSParser]::Tokenize((Get-Content $scriptPath -Raw), [ref]$errors)

    if ($errors) {
        Write-Host "✗ Parse errors found:" -ForegroundColor Red
        foreach ($error in $errors) {
            Write-Host "  Line $($error.StartLine): $($error.Message)" -ForegroundColor Red
        }
        exit 1
    }
    else {
        Write-Host "✓ No parse errors detected" -ForegroundColor Green
    }
}
catch {
    Write-Host "✗ Error during parsing: $_" -ForegroundColor Red
    exit 1
}

# Test 6: Check encoding
$encoding = (Get-Content $scriptPath -Encoding Byte -TotalCount 3 | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
Write-Host "File encoding (first 3 bytes): $encoding" -ForegroundColor Cyan

# Test 7: Validate #Requires statement
$firstLine = Get-Content $scriptPath -TotalCount 1
if ($firstLine -match '#Requires') {
    Write-Host "✓ #Requires statement found: $firstLine" -ForegroundColor Green
}
else {
    Write-Host "⚠ No #Requires statement found" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Syntax validation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "To test script execution (requires Admin):" -ForegroundColor Cyan
Write-Host "  .\Setup-Dependencies-Enhanced.ps1 -Mode Production -WhatIf" -ForegroundColor White

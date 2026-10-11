# Generates a deliberately hostile class list, for exercising roster import.
#
# Every oddity here is something a real registrar's Excel export does, and each one is
# there to break a naive column guess:
#
#   - S/N running sequentially past 100, with a duplicate at row 100 from merged cells
#   - a gap at row 104 where a sheet was filtered
#   - matric numbers in mixed case, with trailing spaces, one blank, one duplicate
#   - names containing commas, in ALL CAPS, with diacritics, hyphenated, padded,
#     one blank and one set of initials
#   - three different date formats in the same column
#   - an email column with " at " instead of "@" from a bad mail merge
#   - a truncated row and a "Total: 120 students" footer
#   - a byte order mark on the comma file, and a semicolon-delimited twin without one
#
# Output is deterministic: the same seed gives the same file on every machine.

$ErrorActionPreference = 'Stop'

$outDir = $PSScriptRoot
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$script:seed = 20210101
function Get-PseudoRandom([int]$max) {
    $script:seed = ($script:seed * 1103515245 + 12345) % 2147483648
    return [int](($script:seed / 2147483648.0) * $max)
}

$surnames = @(
    'Nwosu','Okeke','Adeyemi','Chukwuemeka','Bello','Okafor','Eze','Balogun',
    'Oyelaran','Adesanya','Uche','Nwachukwu','Obi','Adebayo','Sadiq','Ibrahim',
    'Ogunleye','Akinwale','Danladi','Eshun','Fashola','Gbenga','Hassan','Idris'
)

$firstNames = @(
    'Ada','Chidi','Amaka','Tunde','Ngozi','Emeka','Bola','Kemi','Segun','Femi',
    'Chioma','Ibrahim','Yewande','Halima','Uchenna','Adaeze','Olumide','Fatima',
    'Kelechi','Aisha','Obinna','Zainab','Sunday','Maryam','Peter','Grace'
)

$departments = @('Computer Science','Computer Science','Computer Science','COMP. SCI.','Computer Science ')
$levels      = @('100','100','100','100 Level','L100','100')
$genders     = @('M','F','Male','Female','M','F')

function New-StudentName([int]$index) {
    $s = $script:surnames[(Get-PseudoRandom $script:surnames.Count)]
    $f = $script:firstNames[(Get-PseudoRandom $script:firstNames.Count)]

    switch ($index % 17) {
        5  { return "$s, $f" }
        7  { return ($f + ' ' + $s).ToUpperInvariant() }
        9  { return "Ọ̀lọ́run Fẹ́miṣán" }
        11 { return "Oluwaseun $s-Togun" }
        13 { return "  $f $s " }
        default { return "$f $s" }
    }
}

function ConvertTo-CsvField([string]$value) {
    if ($value -match '[",\r\n]') { return '"' + $value.Replace('"', '""') + '"' }
    return $value
}

$headers = @('S/N','Matric No.','Full Name','Gender','Department','Level','Phone Number','Email Address','Date of Registration')
$rows = New-Object System.Collections.Generic.List[string]

$total = 120

for ($i = 1; $i -le $total; $i++) {

    # S/N is the trap: sequential past 100, with a repeat at row 100 caused by merged
    # cells, and a jump at 104 because a row above was filtered out.
    $serial = $i
    if ($i -eq 100) { $serial = 99 }
    if ($i -eq 104) { $serial = 106 }

    $matric = 'CSC/2021/{0:D3}' -f $i

    switch ($i % 23) {
        6  { $matric = $matric.ToLowerInvariant() }
        12 { $matric = "$matric " }
        18 { $matric = '' }
        29 { $matric = 'CSC/2021/029 ' }
    }

    $name = New-StudentName $i
    if ($i -eq 47) { $name = '' }
    if ($i -eq 83) { $name = 'A. C.' }

    $gender     = $genders[(Get-PseudoRandom $genders.Count)]
    $department = $departments[(Get-PseudoRandom $departments.Count)]
    $level      = $levels[(Get-PseudoRandom $levels.Count)]

    $phone = ''
    if ($i % 4 -ne 0) {
        $phone = switch ($i % 3) {
            0 { '0803{0:D8}' -f (Get-PseudoRandom 99999999) }
            1 { '+234803{0:D8}' -f (Get-PseudoRandom 99999999) }
            default { '234803{0:D8}' -f (Get-PseudoRandom 99999999) }
        }
    }

    $email = ''
    if ($i % 5 -ne 0) {
        $email = 'csc2021{0:D3}@uni.edu.ng' -f $i
        if ($i % 25 -eq 0) { $email = $email.Replace('@', ' at ') }
        if ($i % 34 -eq 0) { $email = '' }
    }

    $date = switch ($i % 4) {
        0 { '2021-09-{0:D2}' -f (($i % 28) + 1) }
        1 { '{0:D2}/09/2021' -f (($i % 28) + 1) }
        2 { '14/09/2021' }
        default { '' }
    }

    $cells = @("$serial", (ConvertTo-CsvField $matric), (ConvertTo-CsvField $name), $gender,
               (ConvertTo-CsvField $department), $level, $phone, $email, $date)

    # Somebody deleted cells and saved.
    if ($i -eq 61) { $cells = $cells[0..4] }

    $rows.Add(($cells -join ','))
}

# The bottom of every real spreadsheet.
$rows.Add(',,,120,,,Total: 120 students,,,')

$text = ((@($headers -join ',')) + $rows) -join "`r`n"

# A byte order mark, the way Excel writes one. A parser that does not strip it ends up
# with a header called "ï»¿S/N".
$bytes = [System.Text.Encoding]::UTF8.GetPreamble() + [System.Text.Encoding]::UTF8.GetBytes($text)
[System.IO.File]::WriteAllBytes((Join-Path $outDir 'CSC101-roster.csv'), $bytes)

# The same list out of a different system: semicolons, no BOM, LF endings.
$semicolonLines = @(($headers -join ';')) + ($rows | ForEach-Object {
    $_ -replace ',(?=(?:[^"]*"[^"]*")*[^"]*$)', ';'
})
[System.IO.File]::WriteAllText(
    (Join-Path $outDir 'CSC101-roster-semicolon.csv'),
    ($semicolonLines -join "`n"),
    (New-Object System.Text.UTF8Encoding($false)))

Write-Output "wrote CSC101-roster.csv            ($($rows.Count) rows, $((Get-Item (Join-Path $outDir 'CSC101-roster.csv')).Length) bytes)"
Write-Output "wrote CSC101-roster-semicolon.csv ($($rows.Count) rows, $((Get-Item (Join-Path $outDir 'CSC101-roster-semicolon.csv')).Length) bytes)"
Write-Output ""
Get-Content (Join-Path $outDir 'CSC101-roster.csv') -TotalCount 6
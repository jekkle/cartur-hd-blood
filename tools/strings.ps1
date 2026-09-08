param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string]$Pattern = ".",
    [int]$MinLen = 5,
    [int]$Max = 200
)

# Poor-man's `strings`: pulls printable ASCII runs out of a binary. Needed because the bash
# here has no `strings` binary, and piping it to /dev/null made "command not found" look like
# "no matches".
$bytes = [System.IO.File]::ReadAllBytes($Path)
$sb = New-Object System.Text.StringBuilder
$found = New-Object System.Collections.Generic.HashSet[string]
$hits = 0

for ($i = 0; $i -lt $bytes.Length; $i++) {
    $b = $bytes[$i]
    if ($b -ge 32 -and $b -lt 127) {
        [void]$sb.Append([char]$b)
    } else {
        if ($sb.Length -ge $MinLen) {
            $s = $sb.ToString()
            if ($s -match $Pattern -and $found.Add($s)) {
                Write-Output $s
                $hits++
                if ($hits -ge $Max) { return }
            }
        }
        [void]$sb.Clear()
    }
}
if ($sb.Length -ge $MinLen) {
    $s = $sb.ToString()
    if ($s -match $Pattern -and $found.Add($s)) { Write-Output $s }
}

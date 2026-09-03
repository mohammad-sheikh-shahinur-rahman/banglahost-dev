$ErrorActionPreference = 'Stop'

$emMoji  = "$([char]0x00E2)$([char]0x20AC)$([char]0x201D)"   # â€" == cp1252 of em dash —
$elMoji  = "$([char]0x00E2)$([char]0x20AC)$([char]0x00A6)"   # â€¦ == cp1252 of ellipsis …
$emdash  = [char]0x2014                                       # —
$ellip   = [char]0x2026                                       # …
$pray    = [System.Char]::ConvertFromUtf32(0x1F64F)          # 🙏

function Fix($p, [scriptblock]$body) {
  $bytes  = [System.IO.File]::ReadAllBytes($p)
  $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
  $t = [System.IO.File]::ReadAllText($p)
  $t = & $body $t
  $enc = New-Object System.Text.UTF8Encoding($hasBom)
  [System.IO.File]::WriteAllText($p, $t, $enc)
}

$p = 'I:\BanglaHost\src\BanglaHost.App\Views\SettingsPage.xaml'
Fix $p { param($t) $t.Replace($emMoji, [string]$emdash) }
$r1 = ([regex]::Matches([System.IO.File]::ReadAllText($p), [regex]::Escape($emMoji))).Count

$p = 'I:\BanglaHost\src\BanglaHost.App\Views\SettingsPage.xaml.cs'
Fix $p { param($t) $t.Replace($emMoji, [string]$emdash).Replace($elMoji, [string]$ellip) }
$r2 = ([regex]::Matches([System.IO.File]::ReadAllText($p), [regex]::Escape($emMoji) + '|' + [regex]::Escape($elMoji))).Count

$p = 'I:\BanglaHost\src\BanglaHost.App\Views\DonatePage.xaml'
Fix $p { param($t) $t.Replace('support! ??', 'support! ' + $pray) }
$r3 = ([System.IO.File]::ReadAllText($p) -match 'support! \?\?')

Write-Output ("settings_xaml_remaining={0} settings_cs_remaining={1} donate_still_has_qq={2}" -f $r1, $r2, $r3)

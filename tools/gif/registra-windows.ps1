<#
  Registra Repertum per Windows mentre lavora davvero, per le GIF della documentazione.

  Muove il puntatore vero, clicca davvero e fotografa la finestra a ogni passo.
  Scrive i PNG e frames.csv (file;t_ms;x;y;click) nel formato che legge crea_gif.py.

  Scenari:
    principale  si collega la chiavetta (un'immagine VHD montata in sola lettura),
                Repertum la legge, si preme «Salva copia e hash», fine.
    verifica    si apre la cartella salvata: tutto integro; poi si altera un byte
                di una copia e si preme «Verifica»: il file risulta alterato.

  Serve un Windows con sessione grafica e diritti di amministratore (GitHub Actions va bene).
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $Uscita,
    [Parameter(Mandatory)] [ValidateSet('principale', 'verifica')] [string] $Scenario,
    [string] $Immagine,
    [string] $Cartella,
    [string] $DaAlterare
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool rep);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, int f, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int x, int y, int d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public delegate bool Enum(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, Enum f, IntPtr l);
  public static string Testo(IntPtr h) { var sb = new StringBuilder(4096); SendMessage(h, 0x000D, (IntPtr)4096, sb); return sb.ToString(); }
  public static string Classe(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
  // il primo pulsante visibile con uno dei testi dati ("Verifica" e' anche il titolo di una scheda)
  public static IntPtr Pulsante(IntPtr padre, string[] testi) {
    IntPtr t = IntPtr.Zero;
    EnumChildWindows(padre, (h, l) => {
      if (IsWindowVisible(h) && Classe(h).ToUpperInvariant().Contains("BUTTON") && Array.IndexOf(testi, Testo(h)) >= 0) { t = h; return false; }
      return true; }, IntPtr.Zero);
    return t;
  }
  public static RECT Rett(IntPtr h) {
    RECT r;
    if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0) GetWindowRect(h, out r);
    return r;
  }
  public static void Davanti(IntPtr h) { keybd_event(0x12, 0, 0, IntPtr.Zero); keybd_event(0x12, 0, 2, IntPtr.Zero); ShowWindow(h, 9); SetForegroundWindow(h); }
}
"@

$Uscita = (New-Item -ItemType Directory $Uscita -Force).FullName
$sw = [Diagnostics.Stopwatch]::StartNew()
$script:n = 0
$script:fin = [IntPtr]::Zero
$script:puntatore = $false
$csv = New-Object System.Collections.Generic.List[string]
$csv.Add('file;t_ms;x;y;click')

function Fotogramma([int] $click = 0) {
    $r = [W]::Rett($script:fin)
    $w = $r.R - $r.L; $h = $r.B - $r.T
    if ($w -le 0 -or $h -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $g.Dispose()
    $script:n++
    $nome = 'f{0:D5}.png' -f $script:n
    $bmp.Save((Join-Path $Uscita $nome), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $p = New-Object W+POINT
    [void][W]::GetCursorPos([ref] $p)
    $x = $p.X - $r.L; $y = $p.Y - $r.T
    $xs = ''; $ys = ''
    if ($script:puntatore -and $x -ge 0 -and $y -ge 0 -and $x -lt $w -and $y -lt $h) { $xs = $x; $ys = $y }
    $csv.Add("$nome;$($sw.ElapsedMilliseconds);$xs;$ys;$click")
}

function Attendi($cosa, $sec, [scriptblock] $prova) {
    $fine = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $fine) { $r = & $prova; if ($r) { return $r }; Start-Sleep -Milliseconds 200 }
    throw "timeout: $cosa"
}

# registra finche' la condizione non e' vera
function RegistraFinche($cosa, $sec, [scriptblock] $prova) {
    $fine = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $fine) { Fotogramma; if (& $prova) { Fotogramma; return }; Start-Sleep -Milliseconds 30 }
    throw "timeout: $cosa"
}

function Pausa($ms) {
    $fine = (Get-Date).AddMilliseconds($ms)
    do { Fotogramma; Start-Sleep -Milliseconds 250 } while ((Get-Date) -lt $fine)
}

# sposta il puntatore con una curva morbida, fotografando ogni passo
function Vai($x, $y, $ms = 750) {
    $p = New-Object W+POINT
    [void][W]::GetCursorPos([ref] $p)
    $passi = [Math]::Max(8, [int]($ms / 60))
    for ($i = 1; $i -le $passi; $i++) {
        $t = $i / $passi
        $e = 1 - [Math]::Pow(1 - $t, 3)
        [void][W]::SetCursorPos([int]($p.X + ($x - $p.X) * $e), [int]($p.Y + ($y - $p.Y) * $e))
        Fotogramma
    }
}

# clic vero al centro del pulsante; se la finestra non lo riceve, BM_CLICK
function Clic($pulsante, [scriptblock] $riuscito) {
    $r = [W]::Rett($pulsante)
    Vai ([int](($r.L + $r.R) / 2)) ([int](($r.T + $r.B) / 2))
    Start-Sleep -Milliseconds 150
    Fotogramma
    [W]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [W]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Fotogramma 1
    $fine = (Get-Date).AddSeconds(3)
    while ((Get-Date) -lt $fine) { if (& $riuscito) { return }; Fotogramma; Start-Sleep -Milliseconds 50 }
    'il clic vero non e'' arrivato: uso BM_CLICK'
    [void][W]::PostMessage($pulsante, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
}

function Apri($argomenti) {
    $p = if ($argomenti) { Start-Process $Exe -ArgumentList $argomenti -PassThru } else { Start-Process $Exe -PassThru }
    $script:fin = Attendi 'finestra' 30 { $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $p.MainWindowHandle } }
    [void][W]::MoveWindow($script:fin, 40, 24, 940, 690, $true)
    [W]::Davanti($script:fin)
    Start-Sleep -Milliseconds 800
    $r = [W]::Rett($script:fin)
    [void][W]::SetCursorPos($r.L + 700, $r.T + 470)
    $script:puntatore = $true
    $p
}

function Chiudi($p) {
    $script:puntatore = $false
    [void][W]::PostMessage($script:fin, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    if (-not $p.WaitForExit(30000)) { $p.Kill(); throw 'Repertum non si chiude' }
}

try {
    if ($Scenario -eq 'principale') {
        $p = Apri $null
        $salva = Attendi 'pulsante Salva' 30 { $h = [W]::Pulsante($script:fin, @('Salva copia e hash')); if ($h -ne [IntPtr]::Zero) { $h } }
        $apri = Attendi 'pulsante Apri' 30 { $h = [W]::Pulsante($script:fin, @('Apri cartella')); if ($h -ne [IntPtr]::Zero) { $h } }
        Pausa 2200
        # la chiavetta si "collega": un altro processo monta l'immagine mentre qui si continua a registrare
        $m = Start-Process powershell -ArgumentList '-NoProfile', '-Command', "Mount-DiskImage -ImagePath '$Immagine' -Access ReadOnly | Out-Null" -WindowStyle Hidden -PassThru
        RegistraFinche 'lettura' 300 { [W]::IsWindowEnabled($salva) }
        Pausa 1800
        Clic $salva { [W]::Testo($salva) -ne 'Salva copia e hash' }
        RegistraFinche 'copia' 600 { [W]::IsWindowEnabled($apri) }
        $r = [W]::Rett($script:fin)
        Vai ($r.L + 640) ($r.T + 420) 900
        Pausa 1500
        $m.WaitForExit()
        Dismount-DiskImage -ImagePath $Immagine | Out-Null
        Start-Sleep 2
        Chiudi $p
    }
    else {
        $p = Apri ('"' + $Cartella + '"')
        $vb = Attendi 'pulsante Verifica' 30 { $h = [W]::Pulsante($script:fin, @('Verifica', 'Ferma')); if ($h -ne [IntPtr]::Zero) { $h } }
        RegistraFinche 'prima verifica' 600 { [W]::Testo($vb) -eq 'Verifica' -and [W]::IsWindowEnabled($vb) }
        Pausa 2600
        # si altera un solo byte di una copia
        Set-ItemProperty $DaAlterare -Name IsReadOnly -Value $false
        $fs = [IO.File]::Open($DaAlterare, 'Open', 'ReadWrite')
        $fs.Position = [Math]::Min(1048576, $fs.Length - 1)
        $b = $fs.ReadByte(); $fs.Position = $fs.Position - 1; $fs.WriteByte($b -bxor 0xFF); $fs.Close()
        "alterato un byte di $DaAlterare"
        Clic $vb { [W]::Testo($vb) -ne 'Verifica' }
        RegistraFinche 'seconda verifica' 600 { [W]::Testo($vb) -eq 'Verifica' -and [W]::IsWindowEnabled($vb) }
        $r = [W]::Rett($script:fin)
        Vai ($r.L + 700) ($r.T + 560) 900
        Pausa 1500
        Chiudi $p
    }
}
finally {
    [IO.File]::WriteAllLines((Join-Path $Uscita 'frames.csv'), $csv)
    "$Scenario`: $($script:n) fotogrammi in $([int]($sw.ElapsedMilliseconds / 1000)) s"
}

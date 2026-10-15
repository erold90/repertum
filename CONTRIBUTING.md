# Contribuire

Grazie dell'interesse. Repertum è piccolo di proposito: una finestra, un pulsante, file di
testo che chiunque può aprire. Prima di aggiungere qualcosa, chiediti se lo rende più
semplice o più sicuro per chi lo usa.

## Regole del progetto

- **La chiavetta non si tocca.** Nessuna modifica può scrivere sul supporto collegato,
  nemmeno per prova. Il collaudo controlla che l'immagine del disco simulato abbia la stessa
  impronta prima e dopo.
- **Interfaccia in italiano, con poche parole.** Quello che si può mostrare con un segno non
  si scrive. Niente emoji: le icone sono disegnate nel codice.
- **Generico.** Repertum non chiede chi sei né perché lo usi: niente campi per dati del
  procedimento, dell'operatore o simili.
- **Stessi file su tutte le piattaforme.** `HASH.sha256`, `HASH.md5`, `INVENTARIO.csv`,
  `RIEPILOGO.txt`, `RAPPORTO.html` e `LOG.txt` hanno lo stesso formato su Windows, macOS e
  Linux: il codice che li scrive sta in `src/Repertum.Core` ed è condiviso.

## Compilare

| Piattaforma | Requisiti | Comando |
|---|---|---|
| Windows | Windows 10/11, [.NET SDK](https://dotnet.microsoft.com/download) 8 o successivo | `dotnet build src/Repertum.Windows -c Release -o out` |
| macOS | .NET SDK 8 | `dotnet publish src/Repertum.Desktop -c Release -r osx-arm64` (oppure `osx-x64`) |
| Linux | .NET SDK 8 | `dotnet publish src/Repertum.Desktop -c Release -r linux-x64` |

L'app Windows usa WinForms su .NET Framework 4.8, che Windows ha già: per questo
`Repertum.exe` è un file unico da circa 150 KB. Il Core è scritto in C# 7.3 perché lo
compila anche il progetto Windows.

## Opzioni per lo sviluppo

| Opzione | Cosa fa |
|---|---|
| `--anteprima <cartella>` | Salva le schermate di tutti gli stati con dati finti, poi esce |
| `--istantanee <cartella>` | Durante il lavoro vero salva il registro e una schermata a ogni passaggio |
| `<cartella salvata>` | Apre la verifica di quella cartella |

Su macOS e Linux ci sono anche `--test-dispositivi` (accetta immagini disco e dispositivi
loop come se fossero chiavette), `--automatico` e `--registra <cartella>` (per le GIF).

## Collaudo

Ogni push esegue su GitHub Actions, per Windows, macOS e Linux: compilazione, schermate con
dati finti e il flusso vero su una chiavetta simulata collegata dopo l'avvio. Gli hash
prodotti vengono confrontati con quelli calcolati dagli strumenti del sistema (`Get-FileHash`,
`shasum`, `sha256sum`). Una pull request va bene quando i tre collaudi sono verdi.

## Le GIF della documentazione

Le GIF in `docs/` non sono montate a mano: le registra il workflow `GIF della documentazione`
mentre Repertum lavora davvero. Su Windows il puntatore è quello vero; su macOS e Linux è
sintetico. `tools/gif/crea_gif.py` le compone a partire dai fotogrammi e da `frames.csv`:

```sh
python3 tools/gif/crea_gif.py <cartella-fotogrammi> docs/windows.gif
```

## Messaggi dei commit

[Conventional Commits](https://www.conventionalcommits.org/), in italiano:
`feat: …`, `fix: …`, `docs: …`, `ci: …`.

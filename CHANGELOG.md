# Novità

Il formato segue [Keep a Changelog](https://keepachangelog.com/it/1.1.0/) e le versioni
[Semantic Versioning](https://semver.org/lang/it/).

## [1.2.0] - 2026-10-15

Prima versione pubblica.

### Aggiunto
- **Versioni per macOS** (Apple Silicon e Intel) **e Linux** (x64), con la stessa interfaccia e gli stessi file prodotti della versione Windows.
- macOS: blocco della scrittura con Disk Arbitration, senza password di amministratore. Il montaggio automatico dei dischi esterni viene rifiutato e la chiavetta viene montata in sola lettura (`rdonly`, `noowners`).
- Linux: un aiutante avviato con `pkexec` installa una regola udev temporanea (`UDISKS_IGNORE`, `blockdev --setro`) e monta la chiavetta in sola lettura, con `noload` per ext4. Alla chiusura toglie tutto.
- Collaudo automatico su Windows, macOS e Linux a ogni modifica, con una chiavetta simulata collegata dopo l'avvio.
- GIF della documentazione registrate dal collaudo.

### Modificato
- Hash, report e verifica stanno in `src/Repertum.Core`, condiviso dalle tre versioni.
- `HASH.sha256` e `HASH.md5` usano `/` nei percorsi su tutte le piattaforme, così si controllano anche con `sha256sum -c` e `shasum -c`. La verifica accetta anche le cartelle salvate dalla 1.1.

### Corretto
- Durante la copia l'intestazione («N di M file») restava indietro rispetto alle righe già verificate.

## [1.1.0] - 2026-10-15

### Modificato
- Interfaccia più semplice e generica: niente dati dell'acquisizione da compilare, un solo pulsante «Salva copia e hash», due schede (Chiavetta USB e Verifica), icone disegnate.
- La cartella prende il nome da data, ora ed etichetta della chiavetta.

## [1.0.0] - 2026-10-15

### Aggiunto
- Prima versione, per Windows: blocco della scrittura sui dispositivi USB, controllo della sola lettura, SHA-256 e MD5 di ogni file, copia con verifica, report (`RIEPILOGO.txt`, `RAPPORTO.html`, `HASH.sha256`, `HASH.md5`, `INVENTARIO.csv`, `LOG.txt`) e verifica di una cartella salvata.

[1.2.0]: https://github.com/erold90/repertum/releases/tag/v1.2.0

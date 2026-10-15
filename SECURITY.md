# Sicurezza

Repertum serve a non alterare una chiavetta e a dimostrare che le copie sono identiche
agli originali. Per questo un difetto che tocca **l'integrità** conta più di qualunque
altro, per esempio:

- qualcosa viene scritto sulla chiavetta mentre Repertum è aperto;
- la chiavetta risulta «in sola lettura» quando non lo è;
- un hash è sbagliato, oppure una copia diversa dall'originale viene data per verificata;
- il blocco della scrittura resta attivo, o non torna com'era, dopo la chiusura.

Se trovi un problema di questo tipo **non aprire una issue pubblica**: segnalalo in privato
da [Security → Report a vulnerability](https://github.com/erold90/repertum/security/advisories/new).

Scrivi il sistema operativo e la versione, la versione di Repertum, il modello di chiavetta
e il file system, cosa hai fatto e cosa è successo. Se puoi, allega `LOG.txt` e `RIEPILOGO.txt`
della cartella salvata, dopo aver tolto i dati che non vuoi condividere.

Le correzioni riguardano sempre l'ultima versione pubblicata.

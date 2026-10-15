#!/usr/bin/env python3
# Repertum, collaudo: controlla una cartella salvata da Repertum contro i file d'origine.
#  - ci sono tutti gli allegati;
#  - HASH.sha256 e HASH.md5 usano '/' e contengono ogni file d'origine con gli hash giusti;
#  - le copie in File/ sono identiche agli originali.
import hashlib, json, os, sys, unicodedata

acq, attesi = sys.argv[1], json.load(open(sys.argv[2], encoding='utf-8'))
nfc = lambda s: unicodedata.normalize('NFC', s)
errori = []

for a in ['HASH.sha256', 'HASH.md5', 'INVENTARIO.csv', 'RIEPILOGO.txt', 'RAPPORTO.html', 'LOG.txt', 'File']:
    if not os.path.exists(os.path.join(acq, a)):
        errori.append('manca ' + a)

def elenco(nome):
    d = {}
    for riga in open(os.path.join(acq, nome), encoding='utf-8').read().splitlines():
        if '\\' in riga:
            errori.append(nome + ': barra rovesciata in ' + riga)
        h, n = riga.split('  ', 1)
        d[nfc(n)] = h
    return d

sha, md5 = elenco('HASH.sha256'), elenco('HASH.md5')
if not any('/' in n for n in sha):
    errori.append("HASH.sha256 non usa '/' per le sottocartelle")

for nome, v in attesi.items():
    n = nfc(nome)
    if sha.get(n) != v['sha256']: errori.append('SHA-256 diverso o mancante: ' + nome)
    if md5.get(n) != v['md5']: errori.append('MD5 diverso o mancante: ' + nome)
    copia = os.path.join(acq, 'File', nome)
    if not os.path.exists(copia):
        copia = os.path.join(acq, 'File', unicodedata.normalize('NFD', nome))
    try:
        dati = open(copia, 'rb').read()
        if hashlib.sha256(dati).hexdigest() != v['sha256']: errori.append('copia diversa: ' + nome)
        if os.access(copia, os.W_OK) and os.geteuid() != 0: errori.append('copia scrivibile: ' + nome)
    except OSError as e:
        errori.append('copia illeggibile: %s (%s)' % (nome, e))
    print('OK  ' + nome if not any(nome in e for e in errori) else 'NO  ' + nome)

print(len(sha), 'righe in HASH.sha256,', len(attesi), 'file d\'origine controllati')
for e in errori:
    print('ERRORE: ' + e)
sys.exit(1 if errori else 0)

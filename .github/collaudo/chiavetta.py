#!/usr/bin/env python3
# Repertum, collaudo: i file della chiavetta finta (foto, video, documenti, registrazioni DVR,
# un nome con gli accenti), qualche decina di MB, sempre uguali. Scrive anche attesi.json con
# SHA-256 e MD5 di ciascuno, calcolati sui file d'origine.
import hashlib, json, os, random, sys

cartella, attesi = sys.argv[1], sys.argv[2]
r = random.Random(7)
intestazioni = {
    '.JPG': b'\xff\xd8\xff\xe1\x00\x18Exif\x00\x00', '.png': b'\x89PNG\r\n\x1a\n',
    '.MP4': b'\x00\x00\x00\x20ftypisom\x00\x00\x02\x00', '.mp4': b'\x00\x00\x00\x20ftypmp42\x00\x00\x00\x00',
    '.dav': b'DHAV\xfd\x00\x00\x00', '.pdf': b'%PDF-1.7\n', '.xlsx': b'PK\x03\x04\x14\x00\x06\x00',
}
file = [
    ('DCIM/100MEDIA/IMG_0001.JPG', 2_500_000), ('DCIM/100MEDIA/IMG_0002.JPG', 1_800_000),
    ('DCIM/100MEDIA/VID_0003.MP4', 14_000_000), ('Video/registrazione 21-03.mp4', 9_000_000),
    ('Foto è già/ritratto.png', 900_000), ('DVR/CH01/20250321_183000.dav', 12_000_000),
    ('DVR/CH01/20250321_190000.dav', 7_000_000), ('Documenti/Contratto firmato.pdf', 400_000),
    ('Documenti/Elenco.xlsx', 60_000), ('Documenti/nota;con virgola.txt', None), ('vuoto.dat', 0),
]
elenco = {}
for nome, dim in file:
    p = os.path.join(cartella, nome)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    if dim is None:
        dati = 'Nota del 21 marzo: verificare la registrazione delle 18:30.\n'.encode('utf-8')
    else:
        testa = intestazioni.get(os.path.splitext(nome)[1], b'')
        dati = (testa + r.randbytes(max(0, dim - len(testa)))) if dim else b''
    with open(p, 'wb') as f:
        f.write(dati)
    elenco[nome] = {'sha256': hashlib.sha256(dati).hexdigest(), 'md5': hashlib.md5(dati).hexdigest(), 'byte': len(dati)}
with open(attesi, 'w', encoding='utf-8') as f:
    json.dump(elenco, f, ensure_ascii=False, indent=1)
print(len(elenco), 'file,', sum(v['byte'] for v in elenco.values()), 'byte')

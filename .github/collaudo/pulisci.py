#!/usr/bin/env python3
# Repertum, collaudo (macOS).
# Toglie dal volume appena popolato i file che macOS aggiunge da solo (._*, .fseventsd, .Spotlight-V100,
# .Trashes, .TemporaryItems). Prova il nome NFC e NFD: su exFAT uno dei due non si cancella.
import os, shutil, sys, unicodedata
radice = sys.argv[1]
def via(p):
    for f in (None, 'NFC', 'NFD'):
        q = p if f is None else os.path.join(os.path.dirname(p), unicodedata.normalize(f, os.path.basename(p)))
        try:
            if os.path.isdir(q) and not os.path.islink(q): shutil.rmtree(q)
            else: os.remove(q)
            return True
        except OSError: pass
    print('non rimosso:', p); return False
for n in ['.fseventsd', '.Spotlight-V100', '.Trashes', '.TemporaryItems']:
    if os.path.lexists(os.path.join(radice, n)): via(os.path.join(radice, n))
for d, sub, files in os.walk(radice):
    for n in files + sub:
        if n.startswith('._'): via(os.path.join(d, n))

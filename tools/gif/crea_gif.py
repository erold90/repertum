#!/usr/bin/env python3
"""Compone una GIF animata dai fotogrammi registrati di Repertum.

La cartella dei fotogrammi contiene i PNG e un frames.csv con intestazione
    file;t_ms;x;y;click
dove t_ms sono i millisecondi dall'inizio, x;y la posizione del puntatore
nelle coordinate del PNG (vuoti se il puntatore non c'e') e click=1 nel
fotogramma del clic. Il puntatore e l'onda del clic li disegna questo script:
cosi' la registrazione resta fedele e la GIF si puo' rifare con altri tempi.

Uso:
    python3 crea_gif.py <fotogrammi> <uscita.gif> [--larghezza 880]
                        [--pausa-max 1600] [--fine 3500] [--velocita 1.0]
"""
import argparse
import csv
import os
import sys

from PIL import Image, ImageChops, ImageDraw

# puntatore a freccia, disegnato a 4x e ridotto per avere i bordi morbidi
FRECCIA = [(0, 0), (0, 17), (4, 13), (7, 20), (10, 19), (7, 12), (12, 12)]
SCALA = 4
ONDA = [(70, 6, 200), (60, 11, 150), (60, 16, 100), (60, 21, 55)]  # ms, raggio, opacita'
BLU = (37, 99, 235)


def puntatore():
    lato = 24 * SCALA
    img = Image.new("RGBA", (lato, lato), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    punti = [(x * SCALA + SCALA, y * SCALA + SCALA) for x, y in FRECCIA]
    ombra = [(x + SCALA, y + SCALA) for x, y in punti]
    d.polygon(ombra, fill=(0, 0, 0, 70))
    d.polygon(punti, fill=(255, 255, 255, 255), outline=(0, 0, 0, 255), width=SCALA)
    return img.resize((lato // SCALA, lato // SCALA), Image.LANCZOS)


def onda(img, x, y, raggio, opacita):
    strato = Image.new("RGBA", (img.width * 2, img.height * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(strato)
    cx, cy, r = x * 2, y * 2, raggio * 2
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=BLU + (opacita // 3,), outline=BLU + (opacita,), width=4)
    strato = strato.resize(img.size, Image.LANCZOS)
    return Image.alpha_composite(img, strato)


def leggi(cartella):
    righe = []
    with open(os.path.join(cartella, "frames.csv"), newline="", encoding="utf-8-sig") as f:
        for r in csv.DictReader(f, delimiter=";"):
            righe.append({
                "file": os.path.join(cartella, r["file"]),
                "t": int(r["t_ms"]),
                "x": int(r["x"]) if r.get("x") not in (None, "") else None,
                "y": int(r["y"]) if r.get("y") not in (None, "") else None,
                "click": r.get("click", "0").strip() == "1",
            })
    righe.sort(key=lambda r: r["t"])
    if not righe:
        sys.exit("frames.csv vuoto")
    return righe


def componi(righe, pausa_max, fine, velocita):
    freccia = puntatore()
    uscita = []  # (immagine RGB, durata ms)
    for i, r in enumerate(righe):
        base = Image.open(r["file"]).convert("RGBA")
        durata = fine if i == len(righe) - 1 else (righe[i + 1]["t"] - r["t"]) / velocita
        durata = int(max(40, min(durata, pausa_max if i < len(righe) - 1 else fine)))

        def con_puntatore(img):
            if r["x"] is not None:
                img = img.copy()
                img.alpha_composite(freccia, (r["x"] - 1, r["y"] - 1))
            return img

        if r["click"] and r["x"] is not None:
            for ms, raggio, opacita in ONDA:
                uscita.append((con_puntatore(onda(base, r["x"], r["y"], raggio, opacita)), ms))
            durata = max(40, durata - sum(o[0] for o in ONDA))
        uscita.append((con_puntatore(base), durata))
    return uscita


def unisci_uguali(fotogrammi):
    """Fotogrammi identici consecutivi diventano uno solo, con la somma delle durate."""
    risultato = []
    for img, ms in fotogrammi:
        if risultato and risultato[-1][0].size == img.size and ImageChops.difference(risultato[-1][0], img).getbbox() is None:
            risultato[-1] = (risultato[-1][0], risultato[-1][1] + ms)
        else:
            risultato.append((img, ms))
    return risultato


def tavolozza(immagini):
    """Una sola tavolozza per tutta la GIF: niente sfarfallio dei colori fra un fotogramma e l'altro."""
    campioni = immagini[:: max(1, len(immagini) // 12)][:12]
    w = max(i.width for i in campioni)
    mosaico = Image.new("RGB", (w, sum(i.height for i in campioni)), "white")
    y = 0
    for i in campioni:
        mosaico.paste(i, (0, y))
        y += i.height
    return mosaico.quantize(colors=256, method=Image.Quantize.MEDIANCUT)


def main():
    a = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    a.add_argument("fotogrammi")
    a.add_argument("uscita")
    a.add_argument("--larghezza", type=int, default=880, help="larghezza massima in pixel")
    a.add_argument("--pausa-max", type=int, default=1600, help="durata massima di un fotogramma fermo (ms)")
    a.add_argument("--fine", type=int, default=3500, help="durata dell'ultimo fotogramma (ms)")
    a.add_argument("--velocita", type=float, default=1.0, help=">1 accelera le parti in movimento")
    o = a.parse_args()

    fotogrammi = componi(leggi(o.fotogrammi), o.pausa_max, o.fine, o.velocita)
    rgb = []
    for img, ms in fotogrammi:
        img = img.convert("RGB")
        if img.width > o.larghezza:
            img = img.resize((o.larghezza, round(img.height * o.larghezza / img.width)), Image.LANCZOS)
        rgb.append((img, ms))
    rgb = unisci_uguali(rgb)
    pal = tavolozza([i for i, _ in rgb])
    quadri = [i.quantize(palette=pal, dither=Image.Dither.NONE) for i, _ in rgb]
    quadri[0].save(o.uscita, save_all=True, append_images=quadri[1:], duration=[ms for _, ms in rgb],
                   loop=0, optimize=False, disposal=1)
    totale = sum(ms for _, ms in rgb) / 1000
    print("%s: %d fotogrammi, %.1f s, %.1f MB" % (o.uscita, len(quadri), totale, os.path.getsize(o.uscita) / 1e6))


if __name__ == "__main__":
    main()

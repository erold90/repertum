// Repertum - la descrizione del supporto e l'esito del controllo della sola lettura.

using System;
using System.Collections.Generic;

namespace Repertum
{
    public class InfoSupporto
    {
        public string Lettera, Montaggio, Etichetta, FileSystem, SerialeVolume, Modello, Seriale, Interfaccia, PnpId, VidPid;
        public long CapacitaVolume, Occupato, CapacitaDispositivo;

        public List<KeyValuePair<string, string>> Righe()
        {
            var r = new List<KeyValuePair<string, string>>();
            Action<string, string> a = (k, v) => { if (!string.IsNullOrEmpty(v)) r.Add(new KeyValuePair<string, string>(k, v)); };
            a("Unità", Lettera);
            a("Punto di montaggio", Montaggio);
            a("Modello", Modello);
            a("Numero di serie del dispositivo", Seriale);
            a("Produttore e prodotto USB (VID:PID)", VidPid);
            a("Interfaccia", Interfaccia);
            a("Capacità del dispositivo", CapacitaDispositivo > 0 ? Formati.Byte(CapacitaDispositivo) + " (" + Formati.ByteEsatti(CapacitaDispositivo) + ")" : null);
            a("Etichetta del volume", Etichetta);
            a("File system", FileSystem);
            a("Numero di serie del volume", SerialeVolume);
            a("Capacità del volume", CapacitaVolume > 0 ? Formati.Byte(CapacitaVolume) + " (" + Formati.ByteEsatti(CapacitaVolume) + ")" : null);
            a("Spazio occupato", Formati.Byte(Occupato));
            a(Piattaforma.EtichettaIdentificativo, PnpId);
            return r;
        }
    }

    public class Protezione
    {
        public bool Protetto;
        public bool Verificata;    // false = il controllo non ha potuto rispondere
        public string Dettaglio;
    }
}

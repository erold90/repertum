// Repertum - i testi che dipendono dal sistema operativo. Li imposta ciascuna app all'avvio,
// prima di scrivere qualsiasi file: il Core non sa su quale sistema gira.

namespace Repertum
{
    public static class Piattaforma
    {
        // «Repertum per <Nome> 1.2»
        public static string Nome = "";

        // Nome della riga con l'identificativo del dispositivo nel riepilogo.
        public static string EtichettaIdentificativo = "Identificativo del dispositivo";

        // Le righe «Metodo: ...» del RIEPILOGO.txt (librerie crittografiche usate).
        public static string[] Metodo = new string[0];
    }
}

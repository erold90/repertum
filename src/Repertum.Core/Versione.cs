// Repertum - il numero di versione, unico per le tre app (Windows, macOS, Linux).
// I progetti lo confrontano con il loro <Version> durante la compilazione (Versione.targets).

namespace Repertum
{
    public static class Versione
    {
        public const string Numero = "1.2.0";

        // La forma breve dei testi: «Repertum per Windows 1.2».
        public static readonly string Breve = Accorcia(Numero);

        static string Accorcia(string v)
        {
            var p = v.Split('.');
            return p.Length == 3 && p[2] == "0" ? p[0] + "." + p[1] : v;
        }
    }
}

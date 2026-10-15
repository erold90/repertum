// Repertum - calcolo degli hash in un'unica lettura del file, con la copia facoltativa.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Repertum
{
    [Flags]
    public enum Algoritmi { Nessuno = 0, MD5 = 1, SHA1 = 2, SHA256 = 4, SHA512 = 8 }

    public class Impronte
    {
        public string MD5, SHA1, SHA256, SHA512;
    }

    public static class Calcolo
    {
        public static bool Md5Disponibile = true;
        public const int DimensioneBuffer = 4 * 1024 * 1024;

        static HashAlgorithm Crea(Algoritmi a)
        {
#if NETFRAMEWORK
            // Le classi CryptoServiceProvider sono quelle ammesse anche con il criterio FIPS di Windows;
            // MD5 con FIPS e' vietato: in quel caso si prosegue senza.
            switch (a)
            {
                case Algoritmi.MD5: return new MD5CryptoServiceProvider();
                case Algoritmi.SHA1: return new SHA1CryptoServiceProvider();
                case Algoritmi.SHA256: return new SHA256CryptoServiceProvider();
                default: return new SHA512CryptoServiceProvider();
            }
#else
            // macOS: CommonCrypto; Linux: OpenSSL. Con OpenSSL in modalita' FIPS MD5 puo' mancare.
            switch (a)
            {
                case Algoritmi.MD5: return System.Security.Cryptography.MD5.Create();
                case Algoritmi.SHA1: return System.Security.Cryptography.SHA1.Create();
                case Algoritmi.SHA256: return System.Security.Cryptography.SHA256.Create();
                default: return System.Security.Cryptography.SHA512.Create();
            }
#endif
        }

        static bool Md5Vietato(Exception e)
        {
            return e is InvalidOperationException || e is CryptographicException || e is PlatformNotSupportedException;
        }

        // Una sola lettura del file: tutti gli hash richiesti e, se indicata, la copia.
        public static Impronte Leggi(string sorgente, string copia, Algoritmi richiesti, byte[] buf,
                                     Action<long> avanza, CancellationToken ct)
        {
            var calcoli = new List<KeyValuePair<Algoritmi, HashAlgorithm>>();
            try
            {
                foreach (var a in new[] { Algoritmi.MD5, Algoritmi.SHA1, Algoritmi.SHA256, Algoritmi.SHA512 })
                {
                    if ((richiesti & a) == 0) continue;
                    if (a == Algoritmi.MD5 && !Md5Disponibile) continue;
                    try { calcoli.Add(new KeyValuePair<Algoritmi, HashAlgorithm>(a, Crea(a))); }
                    catch (Exception e) when (a == Algoritmi.MD5 && Md5Vietato(e)) { Md5Disponibile = false; }
                }
                using (var ing = new FileStream(sorgente, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan))
                using (var usc = copia == null ? null : new FileStream(copia, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    int n;
                    while ((n = ing.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var c in calcoli) c.Value.TransformBlock(buf, 0, n, null, 0);
                        if (usc != null) usc.Write(buf, 0, n);
                        if (avanza != null) avanza(n);
                    }
                    foreach (var c in calcoli) c.Value.TransformFinalBlock(buf, 0, 0);
                }
                var r = new Impronte();
                foreach (var c in calcoli)
                {
                    var h = Formati.Esa(c.Value.Hash);
                    if (c.Key == Algoritmi.MD5) r.MD5 = h;
                    else if (c.Key == Algoritmi.SHA1) r.SHA1 = h;
                    else if (c.Key == Algoritmi.SHA256) r.SHA256 = h;
                    else r.SHA512 = h;
                }
                return r;
            }
            finally
            {
                foreach (var c in calcoli) c.Value.Dispose();
            }
        }
    }
}

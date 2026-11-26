using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Lavagon.Core
{
    /// <summary>
    /// Local HTTPS: one "Lavagon Local CA" (etc\ssl\lavagon-ca.crt) signs a certificate per
    /// hostname. Trust the CA once (Menu > Apache > SSL > Add Lavagon CA to Trust Store) and every
    /// https://project.test works without browser warnings.
    /// Certificates are created with the Windows crypto API, no openssl.exe needed.
    /// </summary>
    public static class Certificates
    {
        public static string Dir => Path.Combine(AppPaths.Etc, "ssl");
        public static string CaCert => Path.Combine(Dir, "lavagon-ca.crt");
        private static string CaKey => Path.Combine(Dir, "lavagon-ca.key");

        private static readonly object Sync = new object();

        public static void EnsureCa()
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(CaCert) && File.Exists(CaKey)) return;

                using (var rsa = NewKey())
                {
                    var req = new CertificateRequest(new X500DistinguishedName("CN=Lavagon Local CA, O=Lavagon"),
                        rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
                    req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                    req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
                    using (var ca = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10)))
                    {
                        File.WriteAllText(CaCert, Pem("CERTIFICATE", ca.RawData));
                        File.WriteAllText(CaKey, Pem("PRIVATE KEY", rsa.Key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob)));
                    }
                }
            }
        }

        public static void EnsureFor(string hostname)
        {
            var crtPath = Path.Combine(Dir, hostname + ".crt");
            var keyPath = Path.Combine(Dir, hostname + ".key");
            if (File.Exists(crtPath) && File.Exists(keyPath)) return;
            EnsureCa();

            lock (Sync)
            {
                using (var caCert = new X509Certificate2(Unpem(File.ReadAllText(CaCert))))
                using (var caKey = new RSACng(CngKey.Import(Unpem(File.ReadAllText(CaKey)), CngKeyBlobFormat.Pkcs8PrivateBlob)))
                using (var rsa = NewKey())
                {
                    var req = new CertificateRequest(new X500DistinguishedName("CN=" + hostname),
                        rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                    req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                    req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                        new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                    req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

                    var san = new SubjectAlternativeNameBuilder();
                    san.AddDnsName(hostname);
                    if (hostname == "localhost")
                    {
                        san.AddIpAddress(System.Net.IPAddress.Loopback);
                        san.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
                    }
                    else
                    {
                        san.AddDnsName("*." + hostname);
                    }
                    req.CertificateExtensions.Add(san.Build());

                    var serial = new byte[16];
                    using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(serial);
                    serial[0] &= 0x7F;

                    var notAfter = DateTimeOffset.UtcNow.AddDays(825);
                    if (notAfter > caCert.NotAfter) notAfter = caCert.NotAfter.AddDays(-1);
                    var generator = X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1);
                    using (var cert = req.Create(caCert.SubjectName, generator, DateTimeOffset.UtcNow.AddDays(-1), notAfter, serial))
                    {
                        File.WriteAllText(keyPath, Pem("PRIVATE KEY", rsa.Key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob)));
                        File.WriteAllText(crtPath, Pem("CERTIFICATE", cert.RawData));
                    }
                }
            }
        }

        /// <summary>Adds the Lavagon CA to the current user's trusted roots (Windows asks for confirmation).</summary>
        public static void TrustCa()
        {
            EnsureCa();
            using (var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
            using (var cert = new X509Certificate2(Unpem(File.ReadAllText(CaCert))))
            {
                store.Open(OpenFlags.ReadWrite);
                store.Add(cert);
            }
        }

        private static RSACng NewKey()
        {
            var p = new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.AllowPlaintextExport,
                KeyUsage = CngKeyUsages.AllUsages,
            };
            p.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
            return new RSACng(CngKey.Create(CngAlgorithm.Rsa, null, p));
        }

        private static string Pem(string label, byte[] der)
        {
            var sb = new StringBuilder();
            sb.Append("-----BEGIN ").Append(label).Append("-----\n");
            var b64 = Convert.ToBase64String(der);
            for (int i = 0; i < b64.Length; i += 64) sb.Append(b64.Substring(i, Math.Min(64, b64.Length - i))).Append('\n');
            sb.Append("-----END ").Append(label).Append("-----\n");
            return sb.ToString();
        }

        private static byte[] Unpem(string pem)
        {
            var sb = new StringBuilder();
            foreach (var line in pem.Split('\n'))
            {
                var l = line.Trim();
                if (l.Length == 0 || l.StartsWith("-----")) continue;
                sb.Append(l);
            }
            return Convert.FromBase64String(sb.ToString());
        }
    }
}

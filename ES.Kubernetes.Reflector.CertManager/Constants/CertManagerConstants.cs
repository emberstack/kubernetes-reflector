namespace ES.Kubernetes.Reflector.CertManager.Constants
{
    public static class CertManagerConstants
    {
        public static string CertificateKind => "Certificate";
        public static string CertificatePlural => "certificates";

        public static string CrdGroup => "cert-manager.io";
        public static string CertificateNameLabel => "cert-manager.io/certificate-name";
    }
}
namespace ES.Kubernetes.Reflector.CertManager.Constants
{
    public static class CertManagerConstants
    {
        public static string CrdGroup => "certmanager.k8s.io";
        public static string CertificatePlural => "certificates";
        public static string CertificateNameLabel => "certmanager.k8s.io/certificate-name";
        public static string CertificateKind => "Certificate";
    }
}
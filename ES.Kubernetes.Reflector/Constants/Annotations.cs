namespace ES.Kubernetes.Reflector.Constants
{
    public static class Annotations
    {
        private const string Prefix = "reflector.v1.k8s.emberstack.com";

        public static class Reflection
        {
            public static string Allowed => $"{Prefix}/reflection-allowed";
            public static string AllowedNamespaces => $"{Prefix}/reflection-allowed-namespaces";
            public static string Reflects => $"{Prefix}/reflects";
            public static string ReflectedVersion => $"{Prefix}/reflected-version";
            public static string ReflectedAt => $"{Prefix}/reflected-at";
        }

        public static class CertManagerCertificate
        {
            public static string SecretReflectionAllowed => $"{Prefix}/secret-reflection-allowed";
            public static string SecretReflectionAllowedNamespaces => $"{Prefix}/secret-reflection-allowed-namespaces";
        }
    }
}
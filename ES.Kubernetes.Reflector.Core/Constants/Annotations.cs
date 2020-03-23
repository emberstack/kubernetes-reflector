namespace ES.Kubernetes.Reflector.Core.Constants
{
    public static class Annotations
    {
        public const string Prefix = "reflector.v1.k8s.emberstack.com";

        public static class Reflection
        {
            public static string Allowed => $"{Prefix}/reflection-allowed";
            public static string AllowedNamespaces => $"{Prefix}/reflection-allowed-namespaces";


            public static string AutoEnabled => $"{Prefix}/reflection-auto-enabled";
            public static string AutoNamespaces => $"{Prefix}/reflection-auto-namespaces";


            public static string FortiEnabled => $"{Prefix}/reflection-forti-enabled";
            public static string FortiHosts => $"{Prefix}/reflection-forti-hosts";
            public static string FortiCertificate => $"{Prefix}/reflection-forti-certificate";


            public static string AutoReflects => $"{Prefix}/auto-reflects";
            public static string Reflects => $"{Prefix}/reflects";
            public static string ReflectedVersion => $"{Prefix}/reflected-version";
            public static string ReflectedAt => $"{Prefix}/reflected-at";
        }

        public static class CertManagerCertificate
        {
            public static string SecretReflectionAllowed => $"{Prefix}/secret-reflection-allowed";
            public static string SecretReflectionAllowedNamespaces => $"{Prefix}/secret-reflection-allowed-namespaces";

            public static string SecretReflectionAutoEnabled => $"{Prefix}/secret-reflection-auto-enabled";
            public static string SecretReflectionAutoNamespaces => $"{Prefix}/secret-reflection-auto-namespaces";

            public static string SecretFortiEnabled => $"{Prefix}/secret-reflection-forti-enabled";
            public static string SecretFortiHosts => $"{Prefix}/secret-reflection-forti-hosts";
            public static string SecretFortiCertificate => $"{Prefix}/secret-reflection-forti-certificate";
        }
    }
}
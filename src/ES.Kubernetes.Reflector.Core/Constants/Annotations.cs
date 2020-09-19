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

            #region Ubiquiti

            public static string UbiquitiEnabled => $"{Prefix}/reflection-ubiquiti-enabled";
            public static string UbiquitiHosts => $"{Prefix}/reflection-ubiquiti-hosts";
            public static string UbiquitiCertificate => $"{Prefix}/reflection-ubiquiti-certificate";

            #endregion
            
            #region VMware

            public static string VMwareEnabled => $"{Prefix}/reflection-vmware-enabled";
            public static string VMwareHosts => $"{Prefix}/reflection-vmware-hosts";
            public static string VMwareCertificate => $"{Prefix}/reflection-vmware-certificate";

            #endregion

            #region FreeNAS

            public static string FreeNasEnabled => $"{Prefix}/reflection-freenas-enabled";
            public static string FreeNasHosts => $"{Prefix}/reflection-freenas-hosts";
            public static string FreeNasCertificate => $"{Prefix}/reflection-freenas-certificate";

            #endregion
            
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

            #region Ubiquiti

            public static string SecretUbiquitiEnabled => $"{Prefix}/secret-reflection-ubiquiti-enabled";
            public static string SecretUbiquitiHosts => $"{Prefix}/secret-reflection-ubiquiti-hosts";
            public static string SecretUbiquitiCertificate => $"{Prefix}/secret-reflection-ubiquiti-certificate";

            #endregion
            
            #region VMware

            public static string SecretVMwareEnabled => $"{Prefix}/secret-reflection-vmware-enabled";
            public static string SecretVMwareHosts => $"{Prefix}/secret-reflection-vmware-hosts";
            public static string SecretVMwareCertificate => $"{Prefix}/secret-reflection-vmware-certificate";

            #endregion
            
            #region FreeNAS

            public static string SecretFreeNasEnabled => $"{Prefix}/secret-reflection-freenas-enabled";
            public static string SecretFreeNasHosts => $"{Prefix}/secret-reflection-freenas-hosts";
            public static string SecretFreeNasCertificate => $"{Prefix}/secret-reflection-freenas-certificate";

            #endregion
        }
    }
}
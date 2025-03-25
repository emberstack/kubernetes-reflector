namespace ES.Kubernetes.Reflector.Mirroring.Core;

public static class Annotations
{
    public const string Prefix = "reflector.v1.k8s.emberstack.com";

    public static class Reflection
    {
        public static string Allowed => $"{Prefix}/reflection-allowed";
        public static string AllowedNamespaces => $"{Prefix}/reflection-allowed-namespaces";
        public static string AutoEnabled => $"{Prefix}/reflection-auto-enabled";
        public static string AutoNamespaces => $"{Prefix}/reflection-auto-namespaces";
        public static string Labels => $"{Prefix}/reflection-labels";
        public static string LabelsIncluded => $"{Prefix}/reflection-labels-included";
        public static string Reflects => $"{Prefix}/reflects";


        public static string MetaAutoReflects => $"{Prefix}/auto-reflects";
        public static string MetaReflectedVersion => $"{Prefix}/reflected-version";
        public static string MetaReflectedAt => $"{Prefix}/reflected-at";
    }
}
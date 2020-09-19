using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ES.Kubernetes.Reflector.Core.Constants;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Core.Extensions
{
    public static class MetadataExtensions
    {
        public static V1ObjectMeta Metadata(this IKubernetesObject resource)
        {
            return (V1ObjectMeta) resource.GetType().GetProperty(nameof(V1Namespace.Metadata), typeof(V1ObjectMeta))
                ?.GetValue(resource);
        }

        public static IDictionary<string, string> SafeAnnotations(this V1ObjectMeta metadata)
        {
            return metadata.Annotations ?? new Dictionary<string, string>();
        }

        public static bool ReflectionAllowed(this V1ObjectMeta metadata)
        {
            if (metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.Allowed, out var raw) &&
                bool.TryParse(raw, out var value))
                return value;
            return false;
        }

        public static KubernetesObjectId Reflects(this V1ObjectMeta metadata)
        {
            if (metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.Reflects, out var raw) &&
                KubernetesObjectId.TryParse(raw, out var value))
            {
                if (value.Namespace == null) return new KubernetesObjectId(metadata.NamespaceProperty, value.Name);
                return value;
            }

            return KubernetesObjectId.Empty;
        }

        public static KubernetesObjectId AutoReflects(this V1ObjectMeta metadata)
        {
            if (metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.AutoReflects, out var raw) &&
                KubernetesObjectId.TryParse(raw, out var value))
            {
                if (value.Namespace == null) return new KubernetesObjectId(metadata.NamespaceProperty, value.Name);
                return value;
            }

            return KubernetesObjectId.Empty;
        }

        public static string ReflectionAllowedNamespaces(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.AllowedNamespaces, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? null : raw
                : null;
        }

        public static bool ReflectionAllowedNamespacesMatch(this V1ObjectMeta metadata, string ns)
        {
            var allowedNamespaces = metadata.ReflectionAllowedNamespaces();
            if (allowedNamespaces == null) return true;
            return PatternListMatch(allowedNamespaces, ns);
        }


        public static bool ReflectionVersionMatch(this V1ObjectMeta metadata, string version)
        {
            return metadata.ReflectionVersion() == version;
        }

        public static string ReflectionVersion(this V1ObjectMeta metadata)
        {
            return metadata.Annotations.TryGetValue(Annotations.Reflection.ReflectedVersion, out var revisionValue)
                ? revisionValue
                : string.Empty;
        }

        public static bool ReflectionAutoEnabled(this V1ObjectMeta metadata)
        {
            if (metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.AutoEnabled, out var raw) &&
                bool.TryParse(raw, out var value))
                return value;
            return false;
        }

        public static string ReflectionAutoNamespaces(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.AutoNamespaces, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? null : raw
                : null;
        }

        public static bool ReflectionAutoNamespacesMatch(this V1ObjectMeta metadata, string ns)
        {
            var allowedNamespaces = metadata.ReflectionAutoNamespaces();
            if (allowedNamespaces == null) return true;
            return PatternListMatch(allowedNamespaces, ns);
        }


        private static bool PatternListMatch(string patternList, string value)
        {
            if (string.IsNullOrEmpty(patternList)) return true;
            var regexPatterns = patternList.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries);
            return regexPatterns.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                .Select(pattern => Regex.Match(value, pattern))
                .Any(match => match.Success && match.Value.Length == value.Length);
        }

        public static bool FortiReflectionEnabled(this V1ObjectMeta metadata)
        {
            if (metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.FortiEnabled, out var raw) &&
                bool.TryParse(raw, out var value))
                return value;
            return false;
        }

        public static string[] FortiReflectionHosts(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.FortiHosts, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() :
                raw.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()).Distinct().ToArray()
                : Array.Empty<string>();
        }

        public static string FortiCertificate(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.FortiCertificate, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? null : raw
                : null;
        }

        #region Ubiquiti

        public static bool UbiquitiReflectionEnabled(this V1ObjectMeta metadata)
        {
            if (metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.UbiquitiEnabled, out var raw) &&
                bool.TryParse(raw, out var value))
                return value;
            return false;
        }

        public static string[] UbiquitiReflectionHosts(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.UbiquitiHosts, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() :
                raw.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()).Distinct().ToArray()
                : Array.Empty<string>();
        }

        public static string UbiquitiCertificate(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.UbiquitiCertificate, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? null : raw
                : null;
        }

        #endregion
        
        #region VMware

        public static bool VMwareReflectionEnabled(this V1ObjectMeta metadata)
        {
            if (metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.VMwareEnabled, out var raw) &&
                bool.TryParse(raw, out var value))
                return value;
            return false;
        }

        public static string[] VMwareReflectionHosts(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.VMwareHosts, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() :
                raw.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()).Distinct().ToArray()
                : Array.Empty<string>();
        }

        public static string VMwareCertificate(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.VMwareCertificate, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? null : raw
                : null;
        }

        #endregion
        
        #region FreeNAS

        public static bool FreeNasReflectionEnabled(this V1ObjectMeta metadata)
        {
            if (metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.FreeNasEnabled, out var raw) &&
                bool.TryParse(raw, out var value))
                return value;
            return false;
        }

        public static string[] FreeNasReflectionHosts(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.FreeNasHosts, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() :
                raw.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()).Distinct().ToArray()
                : Array.Empty<string>();
        }

        public static string FreeNasCertificate(this V1ObjectMeta metadata)
        {
            return metadata.SafeAnnotations().TryGetValue(Annotations.Reflection.FreeNasCertificate, out var raw)
                ? string.IsNullOrWhiteSpace(raw) ? null : raw
                : null;
        }

        #endregion
    }
}
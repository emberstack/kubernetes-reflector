# Reflector
Reflector is a Kubernetes addon designed monitor changes to resources (secrets and configmaps) and reflect changes to mirror resources in the same or other namespaces.

### Extensions
Reflector includes a cert-manager extension used to automatically annotate created secrets and allow reflection. See the `cert-manager` extension usage below for more details.

## Deployment

Reflector can be deployed either manually or using Helm (recommended). Each release (found on the [Releases](https://github.com/EmberStack/ES.Kubernetes.Reflector/releases) GitHub page) contains the manual deployment file (`reflector.yaml`) file and packaged Helm chart (`reflector.tgz`).

#### Deployment using Helm

Use Helm to install the latest release packaged chart:
```shellsession
$ helm upgrade --install reflector https://github.com/EmberStack/ES.Kubernetes.Reflector/releases/latest/download/reflector.tgz
```
or download the [latest](https://github.com/EmberStack/ES.Kubernetes.Reflector/releases/latest) `reflector.tgz` packaged chart and apply it:

```shellsession
$ helm upgrade --install reflector reflector.tgz
```

You can customize the values of the helm deployment by using the following Values:

| Parameter                            | Description                                      | Default                                                 |
| ------------------------------------ | ------------------------------------------------ | ------------------------------------------------------- |
| `nameOverride`                       | Overrides release name                           | `""`                                                    |
| `fullnameOverride`                   | Overrides release fullname                       | `""`                                                    |
| `replicaCount`                       | Number of replica.                               | `1`                                                     |
| `image.repository`                   | Container image repository                       | `emberstack/es.kubernetes.reflector`                    |
| `image.tag`                          | Container image tag                              | `latest`                                                |
| `image.pullPolicy`                   | Container image pull policy                      | `Always` if `image.tag` is `latest`, else `IfNotPresent`|
| `extensions.certManager.enabled`     | `cert-manager` addon                             | `true`                                                  |
| `rbac.enabled`                       | Create and use RBAC resources                    | `true`                                                  |
| `serviceAccount.create`              | Create ServiceAccount                            | `true`                                                  |
| `serviceAccount.name`                | ServiceAccount name                              | _release name_                                          |
| `livenessProbe.initialDelaySeconds`  | `livenessProbe` initial delay                    | `5`                                                     |
| `livenessProbe.periodSeconds`        | `livenessProbe` period                           | `10`                                                    |
| `readinessProbe.initialDelaySeconds` | `readinessProbe` initial delay                   | `5`                                                     |
| `readinessProbe.periodSeconds`       | `readinessProbe` period                          | `10`                                                    |
| `resources`                          | Resource limits                                  | `{}`                                                    |
| `nodeSelector`                       | Node labels for pod assignment                   | `{}`                                                    |
| `tolerations`                        | Toleration labels for pod assignment             | `[]`                                                    |
| `affinity`                           | Node affinity for pod assignment                 | `{}`                                                    |




#### Manual deployment
```shellsession
$ kubectl apply -f https://github.com/EmberStack/ES.Kubernetes.Reflector/releases/latest/download/reflector.yaml
```
or by downloading the [latest](https://github.com/EmberStack/ES.Kubernetes.Reflector/releases/latest) `reflector.yaml` file and apply it:

```shellsession
$ kubectl apply -f reflector.yaml
```

## Usage

### 1. Annotate the source secret or configmap
  
  - Add `reflector.v1.k8s.emberstack.com/reflection-allowed: "true"` to the resource annotations to permit reflection to mirrors.
  - Add `reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces: "<list>"` to the resource annotations to permit reflection from only the list of comma separated namespaces or regular expressions. If this annotation is omitted, all   namespaces are allowed.
  
  Example source secret:
   ```yaml
  apiVersion: v1
  kind: Secret
  metadata:
    name: source-secret
    annotations:
      reflector.v1.k8s.emberstack.com/reflection-allowed: "true"
      reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces: "namespace-1,namespace-2,namespace-[0-9]*"
  data:
    ...
  ```
  
  Example source configmap:
   ```yaml
  apiVersion: v1
  kind: ConfigMap
  metadata:
    name: source-config-map
    annotations:
      reflector.v1.k8s.emberstack.com/reflection-allowed: "true"
      reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces: "namespace-1,namespace-2,namespace-[0-9]*"
  data:
    ...
  ```
  
### 2. Annotate the mirror secret or configmap

  - Add `reflector.v1.k8s.emberstack.com/reflects: "<source namespace>/<source name>"` to the mirror object. The value of the annotation is the full name of the source object in `namespace/name` format.
  
  Example mirror secret:
   ```yaml
  apiVersion: v1
  kind: Secret
  metadata:
    name: mirror-secret
    annotations:
      reflector.v1.k8s.emberstack.com/reflects: "default/source-secret"
  data:
    ...
  ```
  
  Example mirror configmap:
   ```yaml
  apiVersion: v1
  kind: ConfigMap
  metadata:
    name: mirror-config-map
    annotations:
      reflector.v1.k8s.emberstack.com/reflects: "default/source-config-map"
  data:
    ...
  ```

### 3. Done!
  Reflector will monitor any changes done to the source objects and copy the following fields:
  - `data` and `type` for secrets
  - `data` and `binaryData` for configmaps
  Reflector keeps track of what was copied by annotating mirrors with the source object version.

 - - - -

## (Optional) `cert-manager` extension
Reflector can automatically annotate secrets created by cert-manager by annotating the `Certificate` object. This allows for issued certificates (example: wildcard certificates) to be reused in other namespaces and permit automatic updates of mirrors on certificate renewal.

  
  - Add `reflector.v1.k8s.emberstack.com/secret-reflection-allowed` to the certificate annotations. Reflector will automatically annotate the resulting secret with `reflector.v1.k8s.emberstack.com/reflection-allowed`.
  - Add `reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces: "<list>"` to the certificate annotations. Reflector will automatically annotate the resulting secret with `reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces`.


In the following example, the generated secret `certificate-secret` will be annotated with the `reflector.v1.k8s.emberstack.com/reflection-allowed` and `reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces` based on the certificate annotations.
```yaml
apiVersion: certmanager.k8s.io/v1alpha1
kind: Certificate
metadata:  
  name: some-certificate
  annotations:
    reflector.v1.k8s.emberstack.com/secret-reflection-allowed: "true"
    reflector.v1.k8s.emberstack.com/secret-reflection-allowed-namespaces: "namespace-1,namespace-2,namespace-[0-9]*"
spec:
  secretName: certificate-secret
  ...
```

Example mirror certificate secret:
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: mirror-certificate-secret
  annotations:
    reflector.v1.k8s.emberstack.com/reflects: "default/certificate-secret"
data:
  ...
```
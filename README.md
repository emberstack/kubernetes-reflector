# Reflector
Reflector is a Kubernetes addon designed to monitor changes to resources (secrets and configmaps) and reflect changes to mirror resources in the same or other namespaces.

[![Build Status](https://dev.azure.com/emberstack/OpenSource/_apis/build/status/kubernetes-reflector?branchName=master)](https://dev.azure.com/emberstack/OpenSource/_build/latest?definitionId=12&branchName=master)
[![Release](https://img.shields.io/github/release/emberstack/kubernetes-reflector.svg?style=flat-square)](https://github.com/emberstack/kubernetes-reflector/releases/latest)
[![GitHub Tag](https://img.shields.io/github/tag/emberstack/kubernetes-reflector.svg?style=flat-square)](https://github.com/emberstack/kubernetes-reflector/releases/latest)
[![Docker Image](https://images.microbadger.com/badges/image/emberstack/kubernetes-reflector.svg)](https://microbadger.com/images/emberstack/kubernetes-reflector)
[![Docker Version](https://images.microbadger.com/badges/version/emberstack/kubernetes-reflector.svg)](https://microbadger.com/images/emberstack/kubernetes-reflector)
[![Docker Pulls](https://img.shields.io/docker/pulls/emberstack/kubernetes-reflector.svg?style=flat-square)](https://hub.docker.com/r/emberstack/kubernetes-reflector)
[![Docker Stars](https://img.shields.io/docker/stars/emberstack/kubernetes-reflector.svg?style=flat-square)](https://hub.docker.com/r/remberstack/kubernetes-reflector)
[![license](https://img.shields.io/github/license/emberstack/kubernetes-reflector.svg?style=flat-square)](LICENSE)


> Supports `amd64`, `arm` and `arm64`

### Extensions
Reflector includes a cert-manager extension used to automatically annotate created secrets and allow reflection. See the `cert-manager` extension usage below for more details.

## Deployment

Reflector can be deployed either manually or using Helm (recommended).

#### Deployment using Helm

Use Helm to install the latest released chart:
```shellsession
$ helm repo add emberstack https://emberstack.github.io/helm-charts
$ helm repo update
$ helm upgrade --install reflector emberstack/reflector
```

You can customize the values of the helm deployment by using the following Values:

| Parameter                            | Description                                      | Default                                                 |
| ------------------------------------ | ------------------------------------------------ | ------------------------------------------------------- |
| `nameOverride`                       | Overrides release name                           | `""`                                                    |
| `fullnameOverride`                   | Overrides release fullname                       | `""`                                                    |
| `image.repository`                   | Container image repository                       | `emberstack/kubernetes-reflector`                       |
| `image.tag`                          | Container image tag                              | `latest`                                                |
| `image.pullPolicy`                   | Container image pull policy                      | `Always` if `image.tag` is `latest`, else `IfNotPresent`|
| `extensions.certManager.enabled`     | `cert-manager` addon                             | `true`                                                  |
| `configuration.logging.minimumLevel` | Logging minimum level                            | `Information`                                           |
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

> Find us on [Helm Hub](https://hub.helm.sh/charts/emberstack)


#### Manual deployment
Each release (found on the [Releases](https://github.com/EmberStack/kubernetes-reflector/releases) GitHub page) contains the manual deployment file (`reflector.yaml`).

```shellsession
$ kubectl apply -f https://github.com/EmberStack/kubernetes-reflector/releases/latest/download/reflector.yaml
```


## Usage

### 1. Annotate the source secret or configmap
  
  - Add `reflector.v1.k8s.emberstack.com/reflection-allowed: "true"` to the resource annotations to permit reflection to mirrors.
  - Add `reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces: "<list>"` to the resource annotations to permit reflection from only the list of comma separated namespaces or regular expressions. If this annotation is omitted or is empty, all namespaces are allowed.

  #### Automatic mirror creation:
  Reflector can create mirrors with the same name in other namespaces automatically. The following annotations control if and how the mirrors are created:
  - Add `reflector.v1.k8s.emberstack.com/reflection-auto-enabled: "true"` to the resource annotations to automatically create mirrors in other namespaces. Note: Requires `reflector.v1.k8s.emberstack.com/reflection-allowed` to be `true` since mirrors need to able to reflect the source.
  - Add `reflector.v1.k8s.emberstack.com/reflection-auto-namespaces: "<list>"` to the resource annotations specify in which namespaces to automatically create mirrors. If this annotation is omitted or is empty, all namespaces are allowed. Note: Namespaces in this list will also be checked by `reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces` since mirrors need to be in namespaces from where reflection is permitted.

  > Important: If the `source` is deleted, automatic mirrors are deleted. Also if either reflection or automirroring is turned off or the automatic mirror's namespace is no longer a valid match for the allowed namespaces, the automatic mirror is deleted.

  > Important: Reflector will skip any conflicting resource when creating auto-mirrors. If there is already a resource with the source's name in a namespace where an automatic mirror is to be created, that namespace is skipped and logged as a warning.
  
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

  > Note: Add `reflector.v1.k8s.emberstack.com/reflected-version: ""` to the resource annotations when doing any manual changes to the mirror (for example when deploying with `helm` or re-applying the deployment script). This will reset the reflected version of the mirror.
  
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
  - `data` for secrets
  - `data` and `binaryData` for configmaps
  Reflector keeps track of what was copied by annotating mirrors with the source object version.

 - - - -

## (Optional) `cert-manager` extension

> The current release supports `cert-manager` from version `0.11.0` or higher. If you're using an older version, please use Reflector version `v2.19193.2`

Reflector can automatically annotate secrets created by cert-manager by annotating the `Certificate` object. This allows for issued certificates (example: wildcard certificates) to be reused in other namespaces and permit automatic updates of mirrors on certificate renewal.
  
  - Add `reflector.v1.k8s.emberstack.com/secret-reflection-allowed` to the certificate annotations. Reflector will automatically annotate the resulting secret with `reflector.v1.k8s.emberstack.com/reflection-allowed`.
  - Add `reflector.v1.k8s.emberstack.com/secret-reflection-allowed-namespaces: "<list>"` to the certificate annotations. Reflector will automatically annotate the resulting secret with `reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces`.
  - Add `reflector.v1.k8s.emberstack.com/secret-reflection-auto-enabled: "true"` to the certificate annotations. Reflector will automatically annotate the resulting secret with `reflector.v1.k8s.emberstack.com/reflection-auto-enabled`.
  - Add `reflector.v1.k8s.emberstack.com/secret-reflection-auto-namespaces: "<list>"` to the certificate annotations. Reflector will automatically annotate the resulting secret with `reflector.v1.k8s.emberstack.com/reflection-auto-namespaces`.


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
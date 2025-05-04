# Reflector
Reflector is a Kubernetes addon designed to monitor changes to resources (secrets and configmaps) and reflect changes to mirror resources in the same or other namespaces.

[![Pipeline](https://github.com/emberstack/kubernetes-reflector/actions/workflows/pipeline.yaml/badge.svg)](https://github.com/emberstack/kubernetes-reflector/actions/workflows/pipeline.yaml)
[![Release](https://img.shields.io/github/release/emberstack/kubernetes-reflector.svg?style=flat-square)](https://github.com/emberstack/kubernetes-reflector/releases/latest)
[![Docker Image](https://img.shields.io/docker/image-size/emberstack/kubernetes-reflector/latest?style=flat-square)](https://hub.docker.com/r/emberstack/kubernetes-reflector)
[![Docker Pulls](https://img.shields.io/docker/pulls/emberstack/kubernetes-reflector?style=flat-square)](https://hub.docker.com/r/emberstack/kubernetes-reflector)
[![license](https://img.shields.io/github/license/emberstack/kubernetes-reflector.svg?style=flat-square)](LICENSE)


> Supports `amd64`, `arm` and `arm64`

## Support
If you need help or found a bug, please feel free to open an Issue on GitHub (https://github.com/emberstack/kubernetes-reflector/issues).  

## Deployment

Reflector can be deployed either manually or using Helm (recommended).

### Prerequisites
- Kubernetes 1.22+
- Helm 3.8+ (if deployed using Helm)

#### Deployment using Helm

Use Helm to install the latest released chart:
```shellsession
$ helm upgrade --install reflector oci://ghcr.io/emberstack/helm-charts/reflector
```
or
```shellsession
$ helm repo add emberstack https://emberstack.github.io/helm-charts
$ helm repo update
$ helm upgrade --install reflector emberstack/reflector
```

You can customize the values of the helm deployment by using the following Values:

| Parameter                                | Description                                      | Default                                                                                          |
| ---------------------------------------- | ------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| `nameOverride`                           | Overrides release name                           | `""`                                                                                             |
| `namespaceOverride`                      | Overrides namespace                              | `""`                                                                                             |
| `fullnameOverride`                       | Overrides release fullname                       | `""`                                                                                             |
| `image.repository`                       | Container image repository                       | `emberstack/kubernetes-reflector` (also available: `ghcr.io/emberstack/kubernetes-reflector`)    |
| `image.tag`                              | Container image tag                              | `Same as chart version`                                                                          |
| `image.pullPolicy`                       | Container image pull policy                      | `IfNotPresent`                                                                                   |
| `configuration.logging.minimumLevel`     | Logging minimum level                            | `Information`                                                                                    |
| `configuration.watcher.timeout`          | Maximum watcher lifetime in seconds              | ``                                                                                               |
| `configuration.kubernetes.skipTlsVerify` | Skip TLS verify when connecting the the cluster  | `false`                                                                                          |
| `rbac.enabled`                           | Create and use RBAC resources                    | `true`                                                                                           |
| `serviceAccount.create`                  | Create ServiceAccount                            | `true`                                                                                           |
| `serviceAccount.name`                    | ServiceAccount name                              | _release name_                                                                                   |
| `livenessProbe.initialDelaySeconds`      | `livenessProbe` initial delay                    | `5`                                                                                              |
| `livenessProbe.periodSeconds`            | `livenessProbe` period                           | `10`                                                                                             |
| `readinessProbe.initialDelaySeconds`     | `readinessProbe` initial delay                   | `5`                                                                                              |
| `readinessProbe.periodSeconds`           | `readinessProbe` period                          | `10`                                                                                             |
| `startupProbe.failureThreshold`          | `startupProbe` failure threshold                 | `10`                                                                                             |
| `startupProbe.periodSeconds`             | `startupProbe` period                            | `5`                                                                                              |
| `resources`                              | Resource limits                                  | `{}`                                                                                             |
| `nodeSelector`                           | Node labels for pod assignment                   | `{}`                                                                                             |
| `tolerations`                            | Toleration labels for pod assignment             | `[]`                                                                                             |
| `affinity`                               | Node affinity for pod assignment                 | `{}`                                                                                             |
| `priorityClassName`                      | `priorityClassName` for pods                     | `""`                                                                                             |
                                         
> Find us on [Artifact Hub](https://artifacthub.io/packages/search?org=emberstack)


#### Manual deployment
Each release (found on the [Releases](https://github.com/emberstack/kubernetes-reflector/releases) GitHub page) contains the manual deployment file (`reflector.yaml`).

```shellsession
$ kubectl -n kube-system apply -f https://github.com/emberstack/kubernetes-reflector/releases/latest/download/reflector.yaml
```


## Usage

### 1. Annotate the source `secret` or `configmap`
  
  - Add `reflector.v1.k8s.emberstack.com/reflection-allowed: "true"` to the resource annotations to permit reflection to mirrors.
  - Add `reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces: "<list>"` to the resource annotations to permit reflection from only the list of comma separated namespaces or regular expressions. Note: If this annotation is omitted or is empty, all namespaces are allowed.

  #### Automatic mirror creation:
  Reflector can create mirrors with the same name in other namespaces automatically. The following annotations control if and how the mirrors are created:
  - Add `reflector.v1.k8s.emberstack.com/reflection-auto-enabled: "true"` to the resource annotations to automatically create mirrors in other namespaces. Note: Requires `reflector.v1.k8s.emberstack.com/reflection-allowed` to be `true` since mirrors need to able to reflect the source.
  - Add `reflector.v1.k8s.emberstack.com/reflection-auto-namespaces: "<list>"` to the resource annotations specify in which namespaces to automatically create mirrors. Note: If this annotation is omitted or is empty, all namespaces are allowed. Namespaces in this list will also be checked by `reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces` since mirrors need to be in namespaces from where reflection is permitted.

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



## `cert-manager` support

> Since version 1.5 of cert-manager you can annotate secrets created from certificates for mirroring using `secretTemplate`  (see https://cert-manager.io/docs/usage/certificate/).

```yaml
apiVersion: cert-manager.io/v1
kind: Certificate
...
spec:
  secretTemplate:
    annotations:
      reflector.v1.k8s.emberstack.com/reflection-allowed: "true"
      reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces: ""
  ...
  ```

=======
> Since version 1.15 of cert-manager you can annotate `Ingress` to create secrets created from certificates for mirroring using `cert-manager.io/secret-template` annotation  (see https://github.com/cert-manager/cert-manager/pull/6839).
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
...
metadata:
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod
    cert-manager.io/secret-template: |
      {"annotations": {"reflector.v1.k8s.emberstack.com/reflection-allowed": "true", "reflector.v1.k8s.emberstack.com/reflection-allowed-namespaces": ""}}
  ...
```

helm upgrade --reuse-values -f ../../../src/configs/prometheus/prometheus-k8s.yml  prometheus-for-amp prometheus


helm repo add grafana https://grafana.github.io/helm-charts
helm repo update
helm install --values values.yaml loki grafana/loki

helm upgrade --reuse-values -f ../../../src/configs/prometheus/prometheus-k8s.yml  prometheus-for-amp prometheus

Write-Host "Installing cert-manager on current cluster"

kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.6.1/cert-manager.yaml


#helm install  cert-manager jetstack/cert-manager   --namespace cert-manager  --create-namespace  --version v1.13.2  --set installCRDs=true


kubectl create namespace observability # <1>
kubectl create -f https://github.com/jaegertracing/jaeger-operator/releases/download/v1.52.0/jaeger-operator.yaml -n observability # <2>
kubectl apply -f all-in-one-jagger-values.yaml

#kubectl delete -n observability -f https://github.com/jaegertracing/jaeger-operator/releases/download/v1.52.0/jaeger-operator.yaml
#kubectl delete -f all-in-one-jagger-values.yaml

kubectl port-forward svc/my-jaeger-query 16685:16685
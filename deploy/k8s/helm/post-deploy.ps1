cd  D:\git_personal\eShopOnContainersAWS\deploy\k8s\nginx-ingress
k apply -f local-cm.yaml

helm upgrade --reuse-values -f ../../../src/configs/prometheus/prometheus-k8s.yml  prometheus-for-amp prometheus


helm repo add grafana https://grafana.github.io/helm-charts
helm repo update
helm install --values loki-values.yml loki grafana/loki


helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm repo update
helm upgrade --reuse-values -f ../../../src/configs/prometheus/prometheus-k8s.yml  prometheus-for-amp prometheus

Write-Host "Installing cert-manager on current cluster"

kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.6.1/cert-manager.yaml


#helm install  cert-manager jetstack/cert-manager   --namespace cert-manager  --create-namespace  --version v1.13.2  --set installCRDs=true


kubectl create namespace observability # <1>
kubectl create -f https://github.com/jaegertracing/jaeger-operator/releases/download/v1.52.0/jaeger-operator.yaml -n observability # <2>
kubectl apply -f all-in-one-jagger-values.yml

#kubectl delete -n observability -f https://github.com/jaegertracing/jaeger-operator/releases/download/v1.52.0/jaeger-operator.yaml
#kubectl delete -f all-in-one-jagger-values.yaml

kubectl port-forward svc/my-jaeger-query 16685:16685


#user: admin
kubectl get secret --namespace default grafana-for-amp -o jsonpath="{.data.admin-password}" | base64 --decode ; echo


kubectl apply -f https://amazon-eks.s3.amazonaws.com/docs/addons-otel-permissions.yaml

aws eks create-addon --addon-name adot --cluster-name clusterC5B25D0D-9f5912074f8a48088d5d4941610b19af 

aws eks describe-addon --addon-name adot --cluster-name clusterC5B25D0D-9f5912074f8a48088d5d4941610b19af 

# helm install my-nginx ingress-nginx/ingress-nginx --namespace nginx-ingress --set controller.metrics.enabled=true --set-string controller.metrics.service.annotations."prometheus\.io/port"="10254" --set-string controller.metrics.service.annotations."prometheus\.io/scrape"="true"


eksctl create iamserviceaccount `
    --name adot-collector `
    # --namespace monitoring `
    --cluster <MY CLUSTER> `
    --attach-policy-arn arn:aws:iam::aws:policy/AmazonPrometheusRemoteWriteAccess `
    --attach-policy-arn arn:aws:iam::aws:policy/AWSXrayWriteOnlyAccess `
    --attach-policy-arn arn:aws:iam::aws:policy/CloudWatchAgentServerPolicy `
    --approve `
    --override-existing-serviceaccounts

k get sa adot-collector -n monitor
k get serviceAccounts -A | grep abot
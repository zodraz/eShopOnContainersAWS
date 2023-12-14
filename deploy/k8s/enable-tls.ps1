Write-Host "Installing cert-manager on current cluster"

kubectl apply  --validate=false -f --validate=false https://github.com/jetstack/cert-manager/releases/download/v0.11.0/cert-manager.yaml


helm install  cert-manager jetstack/cert-manager   --namespace cert-manager  --create-namespace  --version v1.13.2  --set installCRDs=true
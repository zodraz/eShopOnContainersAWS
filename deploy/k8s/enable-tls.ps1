Write-Host "Installing cert-manager on current cluster"

kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.6.1/cert-manager.yaml


#helm install  cert-manager jetstack/cert-manager   --namespace cert-manager  --create-namespace  --version v1.13.2  --set installCRDs=true
Write-Host "Installing cert-manager on current cluster"

kubectl apply  --validate=false -f https://github.com/jetstack/cert-manager/releases/download/v0.11.0/cert-manager.yaml --validate=false

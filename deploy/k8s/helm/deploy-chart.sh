#!/usr/bin/env bash

# http://redsymbol.net/articles/unofficial-bash-strict-mode
set -euo pipefail

usage()
{
  cat <<END
deploy.sh: deploys the $app_name application to a Kubernetes cluster using Helm.
Parameters:
  --eks-name <EKS cluster name>
    The name of the EKS cluster. Required when the registry (using the -r parameter) is set to "eks".
  -c | --chart <name of chart>
    The name of the chart to upgrade (or install)
  -h | --help
    Displays this help text and exits the script.
  -n | --app-name <the name of the app>
    Specifies the name of the application (default: eshop).
  --namespace <namespace name>
    Specifies the namespace name to deploy the app. If it doesn't exists it will be created (default: eshop).
  -p | --docker-password <docker password>
    The Docker password used to logon to the custom registry, supplied using the -r parameter.
  -r | --registry <container registry>
    Specifies the container registry to use (required), e.g. myregistry.azurecr.io.
  --skip-clean
    Do not clean the Kubernetes helm chart. Default is to clean the chart.
  -t | --tag <docker image tag>
    The tag used for the newly created docker images. Default: latest.
  -u | --docker-username <docker username>
    The Docker username used to logon to the custom registry, supplied using the -r parameter.
  --use-local-k8s
    Deploy to a locally installed Kubernetes (default: false).

It is assumed that the Kubernetes cluster has been granted access to the container registry.

WARNING! THE SCRIPT WILL COMPLETELY DESTROY ALL DEPLOYMENTS AND SERVICES VISIBLE
FROM THE CURRENT CONFIGURATION CONTEXT AND NAMESPACE.
It is recommended that you check your selected namespace, 'eshop' by default, is already in use.
Every deployment and service done in the namespace will be deleted.
For more information see https://kubernetes.io/docs/tasks/administer-cluster/namespaces/

END
}

app_name='eshop'
eks_name=''
chart=''
clean='yes'
container_registry=''
docker_password=''
docker_username=''
dns=''
image_tag='latest'
skip_infrastructure=''
use_local_k8s=''
namespace='eshop'

while [[ $# -gt 0 ]]; do
  case "$1" in
    --eks-name )
      eks_name="$2"; shift 2;;
    -c | --chart )
      chart="$2"; shift 2;;
    -h | --help )
      usage; exit 1 ;;
    -n | --app-name )
      app_name="$2"; shift 2;;
    -p | --docker-password )
      docker_password="$2"; shift 2;;
    -r | --registry )
      container_registry="$2"; shift 2;;
    --skip-clean )
      clean=''; shift ;;
    --image-build )
      build_images='yes'; shift ;;
    --image-push )
      push_images='yes'; shift ;;
    --skip-infrastructure )
      skip_infrastructure='yes'; shift ;;
    -t | --tag )
      image_tag="$2"; shift 2;;  
    -u | --docker-username )
      docker_username="$2"; shift 2;;
    --use-local-k8s )
      use_local_k8s='yes'; shift ;;
    --namespace )
      namespace="$2"; shift 2;;
    *)
      echo "Unknown option $1"
      usage; exit 2 ;;
  esac
done

export TAG=$image_tag

# use_custom_registry=''

# if [[ -n $container_registry ]]; then 
#   echo "################ Log into custom registry $container_registry ##################"
#   use_custom_registry='yes'
#   if [[ -z $docker_username ]] || [[ -z $docker_password ]]; then
#     echo "Error: Must use -u (--docker-username) AND -p (--docker-password) if specifying custom registry"
#     exit 1
#   fi
#   docker login -u $docker_username -p $docker_password $container_registry
# fi

ingress_values_file="ingress_values.yaml"

if [[ $use_local_k8s ]]; then
  ingress_values_file="ingress_values_dockerk8s.yaml"
fi

# Initialization & check commands
if [[ -z $dns ]]; then
  echo "No DNS specified. Ingress resources will be bound to public IP."
fi

previous_install=''
if [[ -z $(helm ls -q --namespace $namespace | grep "$app_name-$chart") ]]; then
  echo "No previous release found"
else
  previous_install='yes'
fi
  
if [[ $clean ]] && [[ $previous_install ]]; then
  echo "Cleaning previous helm releases..."
  helm uninstall "$app_name-$chart" --namespace $namespace
  echo "Previous release deleted"
  waitsecs=5; while [ $waitsecs -gt 0 ]; do echo -ne "$waitsecs\033[0K\r"; sleep 1; : $((waitsecs--)); done
  previous_install=''
fi

echo "#################### Begin $app_name $chart installation using Helm ####################"
# if [[ $use_custom_registry ]] || [[ $acr_connected ]]; then
#     if [[ -z $previous_install ]]; then
#       helm install "$app_name-$chart" --namespace $namespace --set "ingress.hosts={$dns}" --set inf.registry.server=$container_registry --values app.yaml --values inf.yaml --values $ingress_values_file --set app.name=$app_name --set inf.k8s.dns=$dns --set image.tag=$image_tag --set image.pullPolicy=Always $chart 
#     else
#       # don't set the image repo since it's already set
#       helm upgrade "$app_name-$chart" --namespace $namespace --set "ingress.hosts={$dns}" --values app.yaml --values inf.yaml --values $ingress_values_file --set app.name=$app_name --set inf.k8s.dns=$dns --set image.tag=$image_tag --set image.pullPolicy=Always $chart 
#     fi
# elif [[ $chart != "eshop-common" ]]; then  # eshop-common is ignored when no secret must be deployed
#   helm upgrade --install "$app_name-$chart" --namespace $namespace --set "ingress.hosts={$dns}" --values app.yaml --values inf.yaml --values $ingress_values_file --set app.name=$app_name --set inf.k8s.dns=$dns --set image.tag=$image_tag --set image.pullPolicy=Always $chart 
# fi

if [[ -z $previous_install ]]; then
  helm install "$app_name-$chart" --namespace $namespace --set "ingress.hosts={$dns}" --set inf.registry.server=$container_registry --values app.yaml --values inf.yaml --values $ingress_values_file --set app.name=$app_name --set inf.k8s.dns=$dns --set image.tag=$image_tag --set image.pullPolicy=Always $chart 
else
  # don't set the image repo since it's already set
  helm upgrade "$app_name-$chart" --namespace $namespace --set "ingress.hosts={$dns}" --values app.yaml --values inf.yaml --values $ingress_values_file --set app.name=$app_name --set inf.k8s.dns=$dns --set image.tag=$image_tag --set image.pullPolicy=Always $chart 
fi

echo "FINISHED: Helm chart installed."
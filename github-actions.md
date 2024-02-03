# eShopOnContainers - GitHub Actions

To configure the github actions it is necessary to add the following secrets to the github repository:

- `USERNAME` : Container registry user. (ECR AWS_ACCESS_KEY_ID)
- `PASSWORD` : Container registry password. (ECR AWS_SECRET_ACCESS_KEY)
- `REGISTRY_ENDPOINT` : Container registry name (eshop in the AWS repo https://gallery.ecr.aws/eshop).
- `REGISTRY_HOST` : public.ecr.aws for ECR.

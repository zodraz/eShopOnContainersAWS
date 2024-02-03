from constructs import Construct
import json

from aws_cdk import (
    aws_secretsmanager as secretsmanager, SecretValue,
    Stack
)


class SecretsManagerStack(Stack):

    def __init__(self, scope: Construct, construct_id: str,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        if (self.node.try_get_context("deploy_secretsmanager") == "True"):

            appsettings_files = self.node.try_get_context(
                "secrets_manager_appsettings_files")

            for key, file_path in appsettings_files.items():
                with open(file_path) as f:
                    secrets = json.load(f)
                secretsmanager.CfnSecret(
                    self,
                    key,
                    name=key,
                    secret_string=json.dumps(secrets)
                )

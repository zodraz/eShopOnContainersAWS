from constructs import Construct
from aws_cdk import (
    Stack, Tags, CfnJson,
    aws_iam as iam
)

from eks_worker_role_statements import EksWorkerRoleStatements

import re


class IamOICProvider(Stack):
    def __init__(self, scope: Construct, construct_id: str, oidc_url: str, env, **kwargs) -> None:
        super().__init__(scope, construct_id, env=env, **kwargs)

        statement = EksWorkerRoleStatements()
        iam_oic = iam.OpenIdConnectProvider(
            self, construct_id,
            url=oidc_url,
            client_ids=['sts.amazonaws.com']
        )
        Tags.of(iam_oic).add(key='cfn.eks.stack', value='iam-pid-stack')

        oidc_provider = re.sub("https://", "", oidc_url)

        def string_like(name_space, sa_name):
            string = CfnJson(
                self, f'JsonCondition{sa_name}',
                value={
                    f'{oidc_provider}:sub': f'system:serviceaccount:{name_space}:{sa_name}',
                    f'{oidc_provider}:aud': 'sts.amazonaws.com'
                }
            )
            return string

        oic_role = iam.Role(
            self, 'EksIAMServiceAccountRole',
            role_name='eshop-eks-oic-dev-sa',
            assumed_by=iam.FederatedPrincipal(
                federated=f'arn:aws:iam::{env.account}:oidc-provider/{oidc_provider}',
                conditions={'StringEquals': string_like(
                    'default', 'eshop-eks-sa')},
                assume_role_action='sts:AssumeRoleWithWebIdentity'
            )
        )

        oic_role.add_managed_policy(
            iam.ManagedPolicy.from_aws_managed_policy_name('AmazonS3ReadOnlyAccess'))

        amazon_dynamodb_full_access = iam.ManagedPolicy.from_aws_managed_policy_name(
            "AmazonDynamoDBFullAccess")
        oic_role.add_managed_policy(amazon_dynamodb_full_access)

        if self.node.try_get_context("deploy_dynamodb") == "True":
            amazon_doc_db_full_access = iam.ManagedPolicy.from_aws_managed_policy_name(
                "AmazonDocDBFullAccess")
            oic_role.add_managed_policy(amazon_doc_db_full_access)

        if self.node.try_get_context("deploy_rabbitmq") == "True":
            amazon_mq_full_access = iam.ManagedPolicy.from_aws_managed_policy_name(
                "AmazonMQFullAccess")
            oic_role.add_managed_policy(amazon_mq_full_access)

        amazon_elastic_cache_full_access = iam.ManagedPolicy.from_aws_managed_policy_name(
            "AmazonElastiCacheFullAccess")
        oic_role.add_managed_policy(amazon_elastic_cache_full_access)

        amazon_rds_full_access = iam.ManagedPolicy.from_aws_managed_policy_name(
            "AmazonRDSFullAccess")
        oic_role.add_managed_policy(amazon_rds_full_access)

        amazon_sqs_full_access = iam.ManagedPolicy.from_aws_managed_policy_name(
            "AmazonSQSFullAccess")
        oic_role.add_managed_policy(amazon_sqs_full_access)

        amazon_sns_full_access = iam.ManagedPolicy.from_aws_managed_policy_name(
            "AmazonSNSFullAccess")
        oic_role.add_managed_policy(amazon_sns_full_access)

        amazon_secretsmanager_rw = iam.ManagedPolicy.from_aws_managed_policy_name(
            "SecretsManagerReadWrite")
        oic_role.add_managed_policy(amazon_secretsmanager_rw)

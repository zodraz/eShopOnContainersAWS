from aws_cdk import (App, Environment)
import os
from eks_cluster import EKSClusterStack
from vpc import VpcStack
from rds_sqlserver import RDSSQLServerStack
from cloudfront_s3 import CloudFrontS3Stack
from elasticcache_redis import ElasticCacheRedisStack
from lambda_function_url import LambdaFunctionUrlStack
from documentdb import DocumentDbStack
# from waf_alb import WAFALBStack
from amq_rabbitmq import AmazonMQRabbitMQStack
from secrets_manager import SecretsManagerStack
from route53 import Route53Stack

app = App()
if app.node.try_get_context("account").strip() != "":
    account = app.node.try_get_context("account")
else:
    account = os.environ.get("CDK_DEPLOY_ACCOUNT",
                             os.environ["CDK_DEFAULT_ACCOUNT"])

if app.node.try_get_context("region").strip() != "":
    region = app.node.try_get_context("region")
else:
    region = os.environ.get("CDK_DEPLOY_REGION",
                            os.environ["CDK_DEFAULT_REGION"])

environment = Environment(account=account, region=region)

route53 = Route53Stack(app, "Route53Stack", env=environment)

cloudfrontS3 = CloudFrontS3Stack(app, "CloudFrontS3Stack", env=environment)

vpc = VpcStack(app, id="VPCStack", env=environment)

rds_sqlserver = RDSSQLServerStack(
    app, "SQLServerStack", vpc.vpc, env=environment)

# Note that if we didn't pass through the ACCOUNT and REGION from these environment variables that
# it won't let us create 3 AZs and will only create a max of 2 - even when we ask for 3 in eks_vpc
eks_cluster_stack = EKSClusterStack(app,
                                    "EKSClusterStack",
                                    vpc.vpc,
                                    env=environment)

elastic_cache_redis = ElasticCacheRedisStack(
    app, "ElasticCacheRedisStack", vpc.vpc, env=environment)

lambda_url = LambdaFunctionUrlStack(
    app, "LambdaFunctionUrlStack", env=environment)

documentdb = DocumentDbStack(
    app, "DocumentDbStack", vpc.vpc, env=environment)

# waf_alb = WAFALBStack(app, "WAFALBStack", alb, env=environment)

amq_rabbitmq = AmazonMQRabbitMQStack(
    app, "AmazonMQRabbitMQStack", vpc.vpc, env=environment)

secrets_manager = SecretsManagerStack(
    app, "SecretsManagerStack", env=environment)

app.synth()

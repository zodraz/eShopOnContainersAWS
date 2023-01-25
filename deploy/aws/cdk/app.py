from aws_cdk import (App, Environment)
import os
from eks_cluster import EKSClusterStack
from vpc import VpcStack
from rds_sqlserver import RDSSQLServerStack

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

vpc = VpcStack(scope=app, id="VPC", env=environment)

rds = RDSSQLServerStack(app, "SQLServer", vpc, env=environment)

# Note that if we didn't pass through the ACCOUNT and REGION from these environment variables that
# it won't let us create 3 AZs and will only create a max of 2 - even when we ask for 3 in eks_vpc
eks_cluster_stack = EKSClusterStack(app,
                                    "EKSClusterStack",
                                    vpc,
                                    env=environment)
app.synth()
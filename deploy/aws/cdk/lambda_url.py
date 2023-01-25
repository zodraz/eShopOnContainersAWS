from aws_cdk import (
    # Duration,
    Stack,
    aws_rds as rds,
    aws_ec2 as ec2,
    aws_iam as iam,
    aws_lambda as fn,
    aws_cloudwatch as cloudwatch,
    aws_ecr as ecr,
    CfnOutput)

from constructs import Construct


class CdkLambdaUrlStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        repository = ecr.Repository.from_repository_name(
            self, id="ecr-repo", repository_name="my repository name")

        docker_image_function = fn.DockerImageFunction(
            self,
            "container-image-lambda-function",
            code=fn.DockerImageCode.from_ecr(repository=repository,
                                             tag_or_digest="latest"))

        fn_url = docker_image_function.add_function_url()
        fn_url.grant_invoke_url(fn.FunctionUrlAuthType.AWS_IAM)

        CfnOutput(
            self,
            "TheUrl",
            # The .url attributes will return the unique Function URL
            value=fn_url.url)

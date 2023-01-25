from unicodedata import name
from aws_solutions_constructs.aws_cloudfront_s3 import CloudFrontToS3, BucketProps
from aws_cdk import (
    Stack, 
    aws_s3, 
    RemovalPolicy,
    aws_s3_deployment as s3deploy)
from constructs import Construct


class CloudFrontS3Stack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        bucket_props = aws_s3.BucketProps(
            bucket_name="eshop_images"
            public_read_access=False,
            block_public_access=aws_s3.BlockPublicAccess.BLOCK_ALL,
            removal_policy=RemovalPolicy.DESTROY,
            access_control=aws_s3.BucketAccessControl.PRIVATE,
            object_ownership=aws_s3.ObjectOwnership.BUCKET_OWNER_ENFORCED,
            encryption=aws_s3.BucketEncryption.S3_MANAGED)

        cloudfronts3=CloudFrontToS3(self, 'eshop-cloudfront-s3', bucket_props=bucket_props)
        

        s3deploy.BucketDeployment(
            self, "DeployCatalogImages",
            sources=[s3deploy.Source.asset('./front-app/build')],
            destination_bucket=cloudfronts3.s3_bucket
        )

from constructs import Construct
from aws_solutions_constructs.aws_cloudfront_s3 import CloudFrontToS3
from aws_cdk import (
    Stack,
    aws_s3,
    RemovalPolicy,
    aws_s3_deployment as s3deploy)


class CloudFrontS3Stack(Stack):

    def __init__(self, scope: Construct, construct_id: str,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        bucket_props = aws_s3.BucketProps(
            public_read_access=True,
            block_public_access=aws_s3.BlockPublicAccess.BLOCK_ACLS,
            removal_policy=RemovalPolicy.DESTROY,
            access_control=aws_s3.BucketAccessControl.PUBLIC_READ,
            # object_ownership=aws_s3.ObjectOwnership.BUCKET_OWNER_ENFORCED,         
            encryption=aws_s3.BucketEncryption.S3_MANAGED)
        
        cloudfronts3 = CloudFrontToS3(
            self, 'eshop-cloudfront-s3', bucket_props = bucket_props)
        
        s3deploy.BucketDeployment(
            self, "DeployCatalogImages",
            sources=[s3deploy.Source.asset(self.node.try_get_context("s3_images_path"))],
            destination_bucket=cloudfronts3.s3_bucket
        )
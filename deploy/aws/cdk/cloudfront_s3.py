# CDK python WAF with CloudFront and regex and ip set rules (v1.102.0 of CDK and above)

from aws_cdk import (
    Duration,
    Stack,
    aws_cloudfront as cloudfront,
    aws_cloudfront_origins as cloudfront_origins,
    aws_s3 as s3,
    aws_certificatemanager as acm,
    aws_route53 as route53,
    aws_wafv2 as wafv2,
    RemovalPolicy,
    aws_route53_targets as targets,
    aws_s3_deployment as s3deploy
)

from constructs import Construct

import json
import jsii

# this is needed to fix a bug in CDK


# @jsii.implements(wafv2.CfnRuleGroup.IPSetReferenceStatementProperty)
# class IPSetReferenceStatement:
#     @property
#     def arn(self):
#         return self._arn

#     @arn.setter
#     def arn(self, value):
#         self._arn = value


class CloudFrontS3Stack(Stack):
    def __init__(self, scope: Construct, construct_id: str, **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        # ##############################################################################
        # # Create the WAF regex pattern and IP sets
        # ##############################################################################

        # ip_set_v4 = wafv2.CfnIPSet(
        #     self,
        #     "IPSetv4",
        #     addresses=[
        #         "1.2.3.4/32",
        #         "5.6.7.8/32",
        #         "0.0.0.0/32"
        #     ],
        #     ip_address_version="IPV4",
        #     scope="CLOUDFRONT",
        # )
        # # note we use the class declared above to get around a bug in CDK
        # ip_ref_statement_v4 = IPSetReferenceStatement()
        # ip_ref_statement_v4.arn = ip_set_v4.attr_arn

        # regex_pattern_set = wafv2.CfnRegexPatternSet(
        #     self,
        #     "RegexPatternSet",
        #     regular_expression_list=["^.*(Mozilla).*$"],
        #     scope="CLOUDFRONT",
        #     description="Checks user-agent for signatures that match devices",
        #     name="device-detector",
        # )

        # regex_statement = (
        #     wafv2.CfnWebACL.RegexPatternSetReferenceStatementProperty(
        #         arn=regex_pattern_set.attr_arn,
        #         field_to_match=wafv2.CfnWebACL.FieldToMatchProperty(
        #             single_header={"Name": "User-Agent"}
        #         ),
        #         text_transformations=[
        #             wafv2.CfnWebACL.TextTransformationProperty(
        #                 priority=0, type="NONE")
        #         ],
        #     )
        # )

        # ##############################################################################
        # # Create the WAF
        # ##############################################################################

        # waf = wafv2.CfnWebACL(
        #     self,
        #     "CloudFrontWebACL",
        #     ####################################################################################
        #     # Set this to allow|block to enable/prevent access to requests not matching a rule
        #     ####################################################################################
        #     default_action=wafv2.CfnWebACL.DefaultActionProperty(block={}),
        #     scope="CLOUDFRONT",
        #     visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
        #         cloud_watch_metrics_enabled=True,
        #         metric_name="WAF",
        #         sampled_requests_enabled=True,
        #     ),
        #     rules=[
        #         # blocks any user agents NOT matching the regex
        #         wafv2.CfnWebACL.RuleProperty(
        #             name="Permitted-User-Agents",
        #             priority=0,
        #             action=wafv2.CfnWebACL.RuleActionProperty(block={}),
        #             visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
        #                 sampled_requests_enabled=True,
        #                 cloud_watch_metrics_enabled=True,
        #                 metric_name="allow-permitted-devices",
        #             ),
        #             statement=wafv2.CfnWebACL.StatementProperty(
        #                 not_statement=wafv2.CfnWebACL.NotStatementProperty(
        #                     statement=wafv2.CfnWebACL.StatementProperty(
        #                         regex_pattern_set_reference_statement=regex_statement
        #                     )
        #                 )
        #             ),
        #         ),
        #         wafv2.CfnWebACL.RuleProperty(
        #             name="Permitted-IPs",
        #             priority=1,
        #             action=wafv2.CfnWebACL.RuleActionProperty(allow={}),
        #             visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
        #                 sampled_requests_enabled=True,
        #                 cloud_watch_metrics_enabled=True,
        #                 metric_name="allow-permitted-ips",
        #             ),
        #             statement=wafv2.CfnWebACL.StatementProperty(
        #                 ip_set_reference_statement=ip_ref_statement_v4
        #             ),
        #         ),
        #         wafv2.CfnWebACL.RuleProperty(
        #             name="AWS-AWSManagedRulesCommonRuleSet",
        #             priority=3,
        #             statement=wafv2.CfnWebACL.StatementProperty(
        #                 managed_rule_group_statement=wafv2.CfnWebACL.ManagedRuleGroupStatementProperty(
        #                     vendor_name="AWS", name="AWSManagedRulesCommonRuleSet"
        #                 )
        #             ),
        #             override_action=wafv2.CfnWebACL.OverrideActionProperty(
        #                 none={}),
        #             visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
        #                 sampled_requests_enabled=True,
        #                 cloud_watch_metrics_enabled=True,
        #                 metric_name="AWS-AWSManagedRulesCommonRuleSet",
        #             ),
        #         ),
        #     ],
        # )

        logs_bucket = s3.Bucket(
            scope=self,
            id='LogsBucket',
            access_control=s3.BucketAccessControl.BUCKET_OWNER_FULL_CONTROL,
            auto_delete_objects=True,
            block_public_access=s3.BlockPublicAccess.BLOCK_ALL,
            encryption=s3.BucketEncryption.S3_MANAGED,
            object_ownership=s3.ObjectOwnership.BUCKET_OWNER_PREFERRED,
            public_read_access=False,
            removal_policy=RemovalPolicy.DESTROY,
            versioned=False
        )

        cloudfront_logs_bucket = s3.Bucket(
            scope=self,
            id='CloudFrontLogsBucket',
            access_control=s3.BucketAccessControl.BUCKET_OWNER_FULL_CONTROL,
            auto_delete_objects=True,
            block_public_access=s3.BlockPublicAccess.BLOCK_ALL,
            encryption=s3.BucketEncryption.S3_MANAGED,
            object_ownership=s3.ObjectOwnership.BUCKET_OWNER_PREFERRED,
            public_read_access=False,
            removal_policy=RemovalPolicy.DESTROY,
            versioned=False
        )

        s3_bucket = s3.Bucket(
            self,
            "DeploymentBucket",
            encryption=s3.BucketEncryption.S3_MANAGED,
            block_public_access=s3.BlockPublicAccess.BLOCK_ALL,
            removal_policy=RemovalPolicy.DESTROY,
            server_access_logs_bucket=logs_bucket
        )

        oai = cloudfront.OriginAccessIdentity(
            self, "OAI", comment="Connects CF with S3"
        )
        s3_bucket.grant_read(oai)

        hosted_zone = route53.HostedZone.from_lookup(
            self, 'HostedZone', domain_name=self.node.try_get_context("dns_domain"))

        certificate = None
        domain_names = None
        if self.node.try_get_context("use_certificate") == "True":
            domain_names = "store." + self.node.try_get_context("dns_domain")
            if self.node.try_get_context("create_new_certificate") == "True":
                certificate = acm.Certificate(
                    self,
                    "Certificate",
                    domain_name=self.node.try_get_context("dns_domain"),
                    validation=acm.CertificateValidation.from_dns(
                        hosted_zone=hosted_zone)
                )
            else:
                certificate = acm.Certificate.from_certificate_arn(self,
                                                                   "Certificate",
                                                                   self.node.try_get_context("certificate_dns_domain_arn"))

        distribution = cloudfront.Distribution(
            self,
            "CloudFrontDistribution",
            # web_acl_id=waf.attr_arn,
            log_bucket=cloudfront_logs_bucket,
            certificate=certificate,
            enable_logging=True,
            default_behavior=cloudfront.BehaviorOptions(
                allowed_methods=cloudfront.AllowedMethods.ALLOW_ALL,
                origin=cloudfront_origins.S3Origin(
                    bucket=s3_bucket,
                    origin_access_identity=oai,
                    origin_path="/",
                ),
                viewer_protocol_policy=cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                compress=True,
                cache_policy=cloudfront.CachePolicy.CACHING_OPTIMIZED
            ),
            domain_names=[domain_names],
            error_responses=[
                cloudfront.ErrorResponse(
                    http_status=404,
                    ttl=Duration.seconds(amount=0)
                )
            ]
        )

        s3deploy.BucketDeployment(
            self, "DeployCatalogImages",
            sources=[s3deploy.Source.asset(
                self.node.try_get_context("s3_images_path"))],
            destination_bucket=s3_bucket,
            distribution=distribution
        )

        route53.ARecord(
            self,
            "StoreARecord",
            zone=hosted_zone,
            record_name="store",
            target=route53.RecordTarget.from_alias(
                targets.CloudFrontTarget(distribution))
        )

        route53.AaaaRecord(
            self,
            "StoreAaaaRecord",
            zone=hosted_zone,
            record_name="store",
            target=route53.RecordTarget.from_alias(
                targets.CloudFrontTarget(distribution))
        )

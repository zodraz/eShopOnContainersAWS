# CDK python WAF with CloudFront and regex and ip set rules (v1.102.0 of CDK and above)

from aws_cdk import (
    Duration,
    Stack,
    aws_cloudfront as cloudfront,
    aws_cloudfront_origins as cloudfront_origins,
    aws_s3 as s3,
    aws_elasticloadbalancingv2 as elb,
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

        if (self.node.try_get_context("deploy_waf_cloudfront") == "True"):

            # ##############################################################################
            # # Create the WAF
            # ##############################################################################

            cfnWebACL = wafv2.CfnWebACL(
                self,
                "CloudFrontWebACL",
                ####################################################################################
                # Set this to allow|block to enable/prevent access to requests not matching a rule
                ####################################################################################
                default_action=wafv2.CfnWebACL.DefaultActionProperty(allow={}),
                scope="CLOUDFRONT",
                visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
                    cloud_watch_metrics_enabled=True,
                    metric_name="WAF",
                    sampled_requests_enabled=True,
                ),
                rules=[
                    # blocks any user agents NOT matching the regex
                    # wafv2.CfnWebACL.RuleProperty(
                    #     name="Permitted-User-Agents",
                    #     priority=0,
                    #     action=wafv2.CfnWebACL.RuleActionProperty(block={}),
                    #     visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
                    #         sampled_requests_enabled=True,
                    #         cloud_watch_metrics_enabled=True,
                    #         metric_name="allow-permitted-devices",
                    #     ),
                    #     statement=wafv2.CfnWebACL.StatementProperty(
                    #         not_statement=wafv2.CfnWebACL.NotStatementProperty(
                    #             statement=wafv2.CfnWebACL.StatementProperty(
                    #                 regex_pattern_set_reference_statement=regex_statement
                    #             )
                    #         )
                    #     ),
                    # ),
                    # wafv2.CfnWebACL.RuleProperty(
                    #     name="Permitted-IPs",
                    #     priority=1,
                    #     action=wafv2.CfnWebACL.RuleActionProperty(allow={}),
                    #     visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
                    #         sampled_requests_enabled=True,
                    #         cloud_watch_metrics_enabled=True,
                    #         metric_name="allow-permitted-ips",
                    #     ),
                    #     statement=wafv2.CfnWebACL.StatementProperty(
                    #         ip_set_reference_statement=ip_ref_statement_v4
                    #     ),
                    # ),
                    # Common Rule Set aligns with major portions of OWASP Core Rule Set
                    wafv2.CfnWebACL.RuleProperty(
                        name="AWS-AWSManagedRulesCommonRuleSet",
                        priority=0,
                        statement=wafv2.CfnWebACL.StatementProperty(
                            managed_rule_group_statement=wafv2.CfnWebACL.ManagedRuleGroupStatementProperty(
                                vendor_name="AWS", name="AWSManagedRulesCommonRuleSet"
                            )
                        ),
                        override_action=wafv2.CfnWebACL.OverrideActionProperty(
                            none={}),
                        visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
                            sampled_requests_enabled=True,
                            cloud_watch_metrics_enabled=True,
                            metric_name="AWS-AWSManagedRulesCommonRuleSet",
                        ),
                    ),
                    # Blocks common SQL Injection
                    wafv2.CfnWebACL.RuleProperty(
                        name="AWS-AWSManagedRulesSQLiRuleSet",
                        priority=1,
                        override_action=wafv2.CfnWebACL.OverrideActionProperty(
                            none={}),
                        statement=wafv2.CfnWebACL.StatementProperty(
                            managed_rule_group_statement=wafv2.CfnWebACL.ManagedRuleGroupStatementProperty(
                                name="AWSManagedRulesSQLiRuleSet",
                                vendor_name="AWS",
                            )
                        ),
                        visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
                            cloud_watch_metrics_enabled=True,
                            metric_name="AWS-AWSManagedRulesSQLiRuleSet",
                            sampled_requests_enabled=True,
                        ),
                    ),
                    # AWS IP Reputation list includes known malicious actors/bots and is regularly updated
                    wafv2.CfnWebACL.RuleProperty(
                        name="AWS-AWSManagedRulesAmazonIpReputationList",
                        priority=2,
                        override_action=wafv2.CfnWebACL.OverrideActionProperty(
                            none={}),
                        statement=wafv2.CfnWebACL.StatementProperty(
                            managed_rule_group_statement=wafv2.CfnWebACL.ManagedRuleGroupStatementProperty(
                                name="AWSManagedRulesAmazonIpReputationList",
                                vendor_name="AWS",
                            )
                        ),
                        visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
                            cloud_watch_metrics_enabled=True,
                            metric_name="AWS-AWSManagedRulesAmazonIpReputationList",
                            sampled_requests_enabled=True,
                        ),
                    )
                ],
            )

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
            access_control=s3.BucketAccessControl.BUCKET_OWNER_FULL_CONTROL,
            auto_delete_objects=True,
            block_public_access=s3.BlockPublicAccess.BLOCK_ALL,
            encryption=s3.BucketEncryption.S3_MANAGED,
            object_ownership=s3.ObjectOwnership.BUCKET_OWNER_PREFERRED,
            server_access_logs_bucket=logs_bucket,
            public_read_access=False,
            removal_policy=RemovalPolicy.DESTROY,
            versioned=False
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
            # domain_names = self.node.try_get_context("dns_domain")
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
            web_acl_id=cfnWebACL.attr_arn if self.node.try_get_context(
                "deploy_waf_cloudfront") == "True" else None,
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

        # if (self.node.try_get_context("deploy_k8s_cloudfront") == "True"):

        #     alb = elb.ApplicationLoadBalancer.from_application_load_balancer_attributes(
        #         self, "K8sELB",
        #         load_balancer_arn=self.node.try_get_context(
        #             "load_balancer_arn"),
        #         security_group_id=self.node.try_get_context(
        #             "load_balancer_security_group_id"),
        #         load_balancer_dns_name=self.node.try_get_context("load_balancer_dns_name"))

        #     eks_dynamic_cache_policy = cloudfront.CachePolicy(
        #         self,
        #         'EKSDynamicCachePolicy',
        #         cache_policy_name='EKSDynamicCachePolicy',
        #         comment='A policy for EKS dynamic origin',
        #         default_ttl=Duration.seconds(0),
        #         min_ttl=Duration.seconds(0),
        #         max_ttl=Duration.days(3600),
        #         cookie_behavior=cloudfront.CacheCookieBehavior.all(),
        #         # header_behavior=cloudfront.CacheHeaderBehavior.allow_list('X-CustomHeader'),
        #         query_string_behavior=cloudfront.CacheQueryStringBehavior.all(),
        #         enable_accept_encoding_gzip=True,
        #         enable_accept_encoding_brotli=True,
        #     )

        #     eks_cache_policy = cloudfront.CachePolicy(
        #         self,
        #         'EKSCachePolicy',
        #         cache_policy_name='EKSCachePolicy',
        #         comment='A policy for EKS origin',
        #         default_ttl=Duration.seconds(3600),
        #         min_ttl=Duration.seconds(3600),
        #         max_ttl=Duration.days(36000),
        #         cookie_behavior=cloudfront.CacheCookieBehavior.all(),
        #         # header_behavior=cloudfront.CacheHeaderBehavior.allow_list('X-CustomHeader'),
        #         query_string_behavior=cloudfront.CacheQueryStringBehavior.all(),
        #         enable_accept_encoding_gzip=True,
        #         enable_accept_encoding_brotli=True,
        #     )

        #     distribution.add_behavior(path_pattern="/",
        #                               origin=cloudfront_origins.LoadBalancerV2Origin(
        #                                   load_balancer=alb,
        #                                   connection_attempts=3,
        #                                   connection_timeout=Duration.seconds(
        #                                       5),
        #                                   read_timeout=Duration.seconds(45),
        #                                   keepalive_timeout=Duration.seconds(
        #                                       45),
        #                                   protocol_policy=cloudfront.OriginProtocolPolicy.HTTP_ONLY
        #                               ),
        #                               allowed_methods=cloudfront.AllowedMethods.ALLOW_ALL,
        #                               viewer_protocol_policy=cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        #                               compress=True,
        #                               cached_methods=cloudfront.CachedMethods.CACHE_GET_HEAD_OPTIONS,
        #                               cache_policy=eks_dynamic_cache_policy)

        #     distribution.add_behavior(path_pattern="*.js",
        #                               origin=cloudfront_origins.LoadBalancerV2Origin(
        #                                   load_balancer=alb,
        #                                   connection_attempts=3,
        #                                   connection_timeout=Duration.seconds(
        #                                       5),
        #                                   read_timeout=Duration.seconds(45),
        #                                   keepalive_timeout=Duration.seconds(
        #                                       45),
        #                                   protocol_policy=cloudfront.OriginProtocolPolicy.HTTP_ONLY
        #                               ),
        #                               allowed_methods=cloudfront.AllowedMethods.ALLOW_ALL,
        #                               viewer_protocol_policy=cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        #                               compress=True,
        #                               cached_methods=cloudfront.CachedMethods.CACHE_GET_HEAD_OPTIONS,
        #                               cache_policy=eks_cache_policy)

        #     distribution.add_behavior(path_pattern="*.css",
        #                               origin=cloudfront_origins.LoadBalancerV2Origin(
        #                                   load_balancer=alb,
        #                                   connection_attempts=3,
        #                                   connection_timeout=Duration.seconds(
        #                                       5),
        #                                   read_timeout=Duration.seconds(45),
        #                                   keepalive_timeout=Duration.seconds(
        #                                       45),
        #                                   protocol_policy=cloudfront.OriginProtocolPolicy.HTTP_ONLY
        #                               ),
        #                               allowed_methods=cloudfront.AllowedMethods.ALLOW_ALL,
        #                               viewer_protocol_policy=cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        #                               compress=True,
        #                               cached_methods=cloudfront.CachedMethods.CACHE_GET_HEAD_OPTIONS,
        #                               cache_policy=eks_cache_policy)

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

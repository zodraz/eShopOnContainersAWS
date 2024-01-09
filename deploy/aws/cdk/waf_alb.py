# CDK python WAF with CloudFront and regex and ip set rules (v1.102.0 of CDK and above)

from aws_cdk import (
    Duration,
    Stack,
    aws_elasticloadbalancingv2 as elbv2,
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


class WAFALBStack(Stack):
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

        if (self.node.try_get_context("deploy_waf_loadbalancer") == "True"):

            # ##############################################################################
            # # Create the WAF
            # ##############################################################################

            cfnWebACL = wafv2.CfnWebACL(
                self,
                "LoadBalancerWebACL",
                ####################################################################################
                # Set this to allow|block to enable/prevent access to requests not matching a rule
                ####################################################################################
                default_action=wafv2.CfnWebACL.DefaultActionProperty(allow={}),
                scope="REGIONAL",
                name="ALB-WAF",
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

            # hosted_zone = route53.HostedZone.from_lookup(
            #     self, 'HostedZone', domain_name=self.node.try_get_context("dns_domain"))

            # certificate = None
            # domain_names = None
            # if self.node.try_get_context("use_certificate") == "True":
            #     domain_names = "store." + \
            #         self.node.try_get_context("dns_domain")
            #     # domain_names = self.node.try_get_context("dns_domain")
            #     if self.node.try_get_context("create_new_certificate") == "True":
            #         certificate = acm.Certificate(
            #             self,
            #             "Certificate",
            #             domain_name=self.node.try_get_context("dns_domain"),
            #             validation=acm.CertificateValidation.from_dns(
            #                 hosted_zone=hosted_zone)
            #         )
            #     else:
            #         certificate = acm.Certificate.from_certificate_arn(self,
            #                                                            "Certificate",
            #                                                            self.node.try_get_context("certificate_dns_domain_arn"))

            alb = elbv2.ApplicationLoadBalancer.from_lookup(
                self, "K8sELB",
                load_balancer_arn=self.node.try_get_context(
                    "load_balancer_arn"))

            wafv2.CfnWebACLAssociation(
                self, "ACLAssociation", resource_arn=alb.load_balancer_arn, web_acl_arn=cfnWebACL.attr_arn)

# from aws_solutions_constructs.aws_route53_alb import Route53ToAlb
# from aws_solutions_constructs.aws_wafwebacl_alb import WafwebaclToAlb
# from aws_cdk import (aws_route53 as route53, Stack)
# from constructs import Construct


# from aws_cdk import (
#     Duration,
#     Stack,
#     aws_cloudfront as cloudfront,
#     aws_cloudfront_origins as cloudfront_origins,
#     aws_s3 as s3,
#     aws_certificatemanager as acm,
#     aws_route53 as route53,
#     aws_wafv2 as wafv2,
#     RemovalPolicy
# )

# class WAFALBStack(Stack):

#     def __init__(self, scope: Construct, construct_id: str, alb,
#                  **kwargs) -> None:
#         super().__init__(scope, construct_id, **kwargs)

#         # Note - all alb constructs turn on ELB logging by default, so require that an environment including account
#         # and region be provided when creating the stack
#         #
#         # MyStack(app, 'id', env=cdk.Environment(account='123456789012', region='us-east-1'))
#         #
#         # This construct can only be attached to a configured Application Load Balancer.
#         WafwebaclToAlb(self,
#                        'test_wafwebacl_alb',
#                        existing_load_balancer_obj=alb)


#         ##############################################################################
#         # Create the WAF regex pattern and IP sets
#         ##############################################################################

#         ip_set_v4 = wafv2.CfnIPSet(
#             self,
#             "IPSetv4",
#             addresses=[
#                 "1.2.3.4/32",
#                 "5.6.7.8/32",
#                 "0.0.0.0/32"
#             ],
#             ip_address_version="IPV4",
#             scope="CLOUDFRONT",
#         )
#         # note we use the class declared above to get around a bug in CDK
#         ip_ref_statement_v4 = IPSetReferenceStatement()
#         ip_ref_statement_v4.arn = ip_set_v4.attr_arn

#         regex_pattern_set = wafv2.CfnRegexPatternSet(
#             self,
#             "RegexPatternSet",
#             regular_expression_list=["^.*(Mozilla).*$"],
#             scope="CLOUDFRONT",
#             description="Checks user-agent for signatures that match devices",
#             name="device-detector",
#         )

#         regex_statement = (
#             wafv2.CfnWebACL.RegexPatternSetReferenceStatementProperty(
#                 arn=regex_pattern_set.attr_arn,
#                 field_to_match=wafv2.CfnWebACL.FieldToMatchProperty(
#                     single_header={"Name": "User-Agent"}
#                 ),
#                 text_transformations=[
#                     wafv2.CfnWebACL.TextTransformationProperty(
#                         priority=0, type="NONE")
#                 ],
#             )
#         )

#         ##############################################################################
#         # Create the WAF
#         ##############################################################################

#         waf = wafv2.CfnWebACL(
#             self,
#             "CloudFrontWebACL",
#             ####################################################################################
#             # Set this to allow|block to enable/prevent access to requests not matching a rule
#             ####################################################################################
#             default_action=wafv2.CfnWebACL.DefaultActionProperty(block={}),
#             scope="CLOUDFRONT",
#             visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
#                 cloud_watch_metrics_enabled=True,
#                 metric_name="WAF",
#                 sampled_requests_enabled=True,
#             ),
#             rules=[
#                 # blocks any user agents NOT matching the regex
#                 wafv2.CfnWebACL.RuleProperty(
#                     name="Permitted-User-Agents",
#                     priority=0,
#                     action=wafv2.CfnWebACL.RuleActionProperty(block={}),
#                     visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
#                         sampled_requests_enabled=True,
#                         cloud_watch_metrics_enabled=True,
#                         metric_name="allow-permitted-devices",
#                     ),
#                     statement=wafv2.CfnWebACL.StatementProperty(
#                         not_statement=wafv2.CfnWebACL.NotStatementProperty(
#                             statement=wafv2.CfnWebACL.StatementProperty(
#                                 regex_pattern_set_reference_statement=regex_statement
#                             )
#                         )
#                     ),
#                 ),
#                 wafv2.CfnWebACL.RuleProperty(
#                     name="Permitted-IPs",
#                     priority=1,
#                     action=wafv2.CfnWebACL.RuleActionProperty(allow={}),
#                     visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
#                         sampled_requests_enabled=True,
#                         cloud_watch_metrics_enabled=True,
#                         metric_name="allow-permitted-ips",
#                     ),
#                     statement=wafv2.CfnWebACL.StatementProperty(
#                         ip_set_reference_statement=ip_ref_statement_v4
#                     ),
#                 ),
#                 wafv2.CfnWebACL.RuleProperty(
#                     name="AWS-AWSManagedRulesCommonRuleSet",
#                     priority=3,
#                     statement=wafv2.CfnWebACL.StatementProperty(
#                         managed_rule_group_statement=wafv2.CfnWebACL.ManagedRuleGroupStatementProperty(
#                             vendor_name="AWS", name="AWSManagedRulesCommonRuleSet"
#                         )
#                     ),
#                     override_action=wafv2.CfnWebACL.OverrideActionProperty(
#                         none={}),
#                     visibility_config=wafv2.CfnWebACL.VisibilityConfigProperty(
#                         sampled_requests_enabled=True,
#                         cloud_watch_metrics_enabled=True,
#                         metric_name="AWS-AWSManagedRulesCommonRuleSet",
#                     ),
#                 ),
#             ],
#         )

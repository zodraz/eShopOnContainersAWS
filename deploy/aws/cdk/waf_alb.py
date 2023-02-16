from aws_solutions_constructs.aws_route53_alb import Route53ToAlb
from aws_solutions_constructs.aws_wafwebacl_alb import WafwebaclToAlb
from aws_cdk import (aws_route53 as route53, Stack)
from constructs import Construct


class WAFALBStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, alb,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        # Note - all alb constructs turn on ELB logging by default, so require that an environment including account
        # and region be provided when creating the stack
        #
        # MyStack(app, 'id', env=cdk.Environment(account='123456789012', region='us-east-1'))
        #
        # This construct can only be attached to a configured Application Load Balancer.
        WafwebaclToAlb(self,
                       'test_wafwebacl_alb',
                       existing_load_balancer_obj=alb)

from constructs import Construct

from aws_cdk import (
    aws_route53 as route53,
    Stack,
    CfnOutput,
    Fn
)


class Route53Stack(Stack):

    def __init__(self, scope: Construct, construct_id: str,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        if self.node.try_get_context("create_new_dns_domain") == "True":
            zone = route53.PublicHostedZone(self, "HostedZone",
                                            zone_name=self.node.try_get_context("dns_domain"))
        else:
            zone = route53.HostedZone.from_lookup(
                self, 'HostedZone', domain_name=self.node.try_get_context("dns_domain"))

        CfnOutput(
            self,
            id="HostedZoneId",
            value=zone.hosted_zone_id,
            description="Hosted Zone Id"
        )

        # CfnOutput(
        #     self,
        #     id="NameServer0",
        #     value=Fn.select(0, zone.hosted_zone_name_servers),
        #     description="NameServer 0"
        # )

        # CfnOutput(
        #     self,
        #     id="NameServe1",
        #     value=Fn.select(1, zone.hosted_zone_name_servers),
        #     description="NameServer 1"
        # )

        # CfnOutput(
        #     self,
        #     id="NameServer2",
        #     value=Fn.select(2, zone.hosted_zone_name_servers),
        #     description="NameServer 2"
        # )

        # CfnOutput(
        #     self,
        #     id="NameServer3",
        #     value=Fn.select(3, zone.hosted_zone_name_servers),
        #     description="NameServer 3"
        # )

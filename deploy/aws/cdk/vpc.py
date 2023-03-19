from aws_cdk import aws_ec2 as ec2, Stack, CfnOutput
from constructs import Construct


class VpcStack(Stack):

    def __init__(self, scope: Construct, id: str, **kwargs) -> None:
        super().__init__(scope, id, **kwargs)

        # Either create a new VPC with the options below OR import an existing one by name
        if self.node.try_get_context("create_new_vpc") == "True":

            if self.node.try_get_context("vpc_only_public") == "True":
                subnets_configuration = [ec2.SubnetConfiguration(
                    subnet_type=ec2.SubnetType.PUBLIC,
                    name="Public",
                    cidr_mask=self.node.try_get_context(
                        "vpc_cidr_mask_public_only"),
                )]
            else:
                subnets_configuration = [  # 3 x Public Subnets (1 per AZ) with 256 IPs each for our ALBs and NATs
                    ec2.SubnetConfiguration(
                        subnet_type=ec2.SubnetType.PUBLIC,
                        name="Public",
                        cidr_mask=self.node.try_get_context(
                            "vpc_cidr_mask_public"),
                    ),

                    # 3 x Private Subnets (1 per AZ) with 256 IPs each for our Nodes and Pods
                    ec2.SubnetConfiguration(
                        subnet_type=ec2.SubnetType.PRIVATE_WITH_EGRESS,
                        name="Private",
                        cidr_mask=self.node.try_get_context(
                            "vpc_cidr_mask_private"),
                    )]

            self.vpc = ec2.Vpc(
                self,
                "VPC",
                # vpc_name="vpc-eu-central-1-eshop-aws"
                # We are choosing to spread our VPC across 3 availability zones
                max_azs=3,
                nat_gateways=self.node.try_get_context(
                    "eks_nat_gateways_quantity"),
                # We are creating a VPC that has a /22, 1024 IPs, for our EKS cluster.
                # I am using that instead of a /16 etc. as I know many companies have constraints here
                # If you can go bigger than this great - but I would try not to go much smaller if you can
                # I use https://www.davidc.net/sites/default/subnets/subnets.html to me work out the CIDRs
                ip_addresses=ec2.IpAddresses.cidr(
                    self.node.try_get_context("vpc_cidr")),
                subnet_configuration=subnets_configuration
            )
        else:
            self.vpc = ec2.Vpc.from_lookup(
                self,
                "VPC",
                vpc_name=self.node.try_get_context("existing_vpc_name"))

        CfnOutput(
            self,
            id="VPCId",
            value=self.vpc.vpc_id,
            description="VPC ID",
            export_name=f"{self.region}:{self.account}:{self.stack_name}:vpc-id"
        )

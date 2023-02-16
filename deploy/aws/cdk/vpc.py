from aws_cdk import aws_ec2 as ec2, Stack, Tag, CfnOutput
from constructs import Construct


class VpcStack(Stack):

    def __init__(self, scope: Construct, id: str, **kwargs) -> None:
        super().__init__(scope, id, **kwargs)

        # Either create a new VPC with the options below OR import an existing one by name
        if self.node.try_get_context("create_new_vpc") == "True":
            if self.node.try_get_context("create_vpc_nat_gateway") == "True":
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
                    subnet_configuration=[
                        # 3 x Public Subnets (1 per AZ) with 256 IPs each for our ALBs and NATs
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
                        ),
                    ],
                )
            else:
                self.vpc = ec2.Vpc(
                    self,
                    "VPC",
                    # We are choosing to spread our VPC across 3 availability zones
                    max_azs=3,
                    # We are creating a VPC that has a /22, 1024 IPs, for our EKS cluster.
                    # I am using that instead of a /16 etc. as I know many companies have constraints here
                    # If you can go bigger than this great - but I would try not to go much smaller if you can
                    # I use https://www.davidc.net/sites/default/subnets/subnets.html to me work out the CIDRs
                    ip_addresses=ec2.IpAddresses.cidr(
                        self.node.try_get_context("vpc_cidr")),
                    subnet_configuration=[
                        # 3 x Public Subnets (1 per AZ) with 64 IPs each for our ALBs and NATs
                        ec2.SubnetConfiguration(
                            subnet_type=ec2.SubnetType.PUBLIC,
                            name="Public",
                            cidr_mask=self.node.try_get_context(
                                "vpc_cidr_mask_public"),
                        ),
                        # 3 x Private Subnets (1 per AZ) with 256 IPs each for our Nodes and Pods
                        ec2.SubnetConfiguration(
                            subnet_type=ec2.SubnetType.PRIVATE_ISOLATED,
                            name="Private",
                            cidr_mask=self.node.try_get_context(
                                "vpc_cidr_mask_private"),
                        ),
                    ],
                )
        else:
            self.vpc = ec2.Vpc.from_lookup(
                self,
                "VPC",
                vpc_name=self.node.try_get_context("existing_vpc_name"))

        # self.vpc = ec2.Vpc(
        #     self,
        #     id="VPC",
        #     cidr="10.0.0.0/16",
        #     max_azs=2,
        #     nat_gateways=1,
        #     subnet_configuration=[
        #         ec2.SubnetConfiguration(
        #             name="public", cidr_mask=24,
        #             reserved=False, subnet_type=ec2.SubnetType.PUBLIC),
        #         ec2.SubnetConfiguration(
        #             name="private", cidr_mask=24,
        #             reserved=False, subnet_type=ec2.SubnetType.PRIVATE),
        #         ec2.SubnetConfiguration(
        #             name="DB", cidr_mask=24,
        #             reserved=False, subnet_type=ec2.SubnetType.ISOLATED
        #         ),
        #         # ec2.SubnetConfiguration(
        #         #     name="DB2", cidr_mask=24,
        #         #     reserved=False, subnet_type=ec2.SubnetType.ISOLATED
        #         # )
        #     ],
        #     enable_dns_hostnames=True,
        #     enable_dns_support=True
        # )

        Tag(key="Application", value=self.stack_name)
        Tag("Network", "Public")
        # core.Tag("Name", "VPCName-Environment").add(vpc)
        # core.Tag("Environment", "Environment").add(vpc)

        # bastion = ec2.BastionHostLinux(
        #     self,
        #     id="BastionHost",
        #     vpc=self.vpc,
        #     instance_name="BastionHost",
        #     instance_type=ec2.InstanceType(ec2_type),
        #     subnet_selection=ec2.SubnetSelection(
        #         subnet_type=ec2.SubnetType.PUBLIC))
        # bastion.allow_ssh_access_from(ec2.Peer.any_ipv4())

        # # Setup key_name for EC2 instance login if you don't use Session Manager
        # #bastion.instance.instance.add_property_override("KeyName", key_name)

        # ec2.CfnEIP(self,
        #            id="BastionHostEIP",
        #            domain="vpc",
        #            instance_id=bastion.instance_id)

        CfnOutput(
            self,
            id="VPCId",
            value=self.vpc.vpc_id,
            description="VPC ID",
            export_name=f"{self.region}:{self.account}:{self.stack_name}:vpc-id"
        )
        
        # self.vpc.private_subnets

        # core.CfnOutput(
        #     self,
        #     id="BastionPrivateIP",
        #     value=bastion.instance_private_ip,
        #     description="BASTION Private IP",
        #     export_name=
        #     f"{self.region}:{self.account}:{self.stack_name}:bastion-private-ip"
        # )

        # core.CfnOutput(
        #     self,
        #     id="BastionPublicIP",
        #     value=bastion.instance_public_ip,
        #     description="BASTION Public IP",
        #     export_name=
        #     f"{self.region}:{self.account}:{self.stack_name}:bastion-public-ip"
        # )

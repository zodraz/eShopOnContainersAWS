from aws_cdk import (
    Stack,
    aws_ec2 as ec2,
    aws_docdb as docdb)

from constructs import Construct


class DocumentDbStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc: ec2.Vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        if (self.node.try_get_context("deploy_documentdb") == "True"):

            db_security_group = ec2.SecurityGroup(self,
                                                  "DocDBSecurityGroup",
                                                  vpc=vpc,
                                                  allow_all_outbound=True)

            db_security_group.add_ingress_rule(
                peer=ec2.Peer.ipv4(self.node.try_get_context(
                    "vpc_cidr_mask_docdb")),
                description="DocDB",
                connection=ec2.Port.tcp(27017))

            if self.node.try_get_context("vpc_only_public") == "False":
                vpc_subnet = ec2.SubnetSelection(
                    subnet_type=ec2.SubnetType.PRIVATE_WITH_EGRESS)
            else:
                vpc_subnet = ec2.SubnetSelection(
                    subnet_type=ec2.SubnetType.PUBLIC)

            cluster = docdb.DatabaseCluster(self, "Database",
                                            master_user=docdb.Login(
                                                username="eshop",  # NOTE: 'admin' is reserved by DocumentDB
                                                secret_name="/eshop/docdb/masteruser"
                                            ),
                                            instance_type=ec2.InstanceType.of(
                                                ec2.InstanceClass.MEMORY5, ec2.InstanceSize.LARGE),
                                            vpc_subnets=vpc_subnet,
                                            vpc=vpc,
                                            instances=self.node.try_get_context(
                                                "documentdb_quantity"),
                                            deletion_protection=False,
                                            export_profiler_logs_to_cloud_watch=True,  # Enable sending profiler logs
                                            export_audit_logs_to_cloud_watch=True,  # Enable sending audit logs
                                            )

            cluster.connections.add_security_group(db_security_group)

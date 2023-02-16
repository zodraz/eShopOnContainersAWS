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

            cluster = docdb.DatabaseCluster(self, "Database",
                                            master_user=docdb.Login(
                                                username="eshop",  # NOTE: 'admin' is reserved by DocumentDB
                                                secret_name="/eshop/docdb/masteruser"
                                            ),
                                            instance_type=ec2.InstanceType.of(
                                                ec2.InstanceClass.MEMORY5, ec2.InstanceSize.LARGE),
                                            vpc_subnets=ec2.SubnetSelection(
                                                subnet_type=ec2.SubnetType.PRIVATE_ISOLATED
                                            ),
                                            vpc=vpc,
                                            instances=1,
                                            deletion_protection=False,
                                            export_profiler_logs_to_cloud_watch=True,  # Enable sending profiler logs
                                            export_audit_logs_to_cloud_watch=True,  # Enable sending audit logs
                                            )
            
            docdb_user_secret = docdb.DatabaseSecret(self, "DocdbUserSecret",
                username="eshop",
                secret_name="/eshop/docdb/masteruser"
            )
            _ = docdb_user_secret.attach(cluster) # Adds DB connections information in the secret

            cluster.add_rotation_multi_user("eshop", 
             secret=docdb_user_secret)

            db_security_group = ec2.SecurityGroup(self,
                                              "DocDbSecurityGroup",
                                              vpc=vpc,
                                              allow_all_outbound=True)

            db_security_group.add_ingress_rule(
                peer=ec2.Peer.ipv4(self.node.try_get_context(
                    "vpc_cidr_mask_docdb")),
                description="DocDb",
                connection=ec2.Port.tcp(27017))
        
            cluster.connections.add_security_group(db_security_group)

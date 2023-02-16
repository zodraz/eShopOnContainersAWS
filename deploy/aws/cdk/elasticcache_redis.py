from constructs import Construct
from aws_cdk import (
    aws_elasticache,
    aws_ec2 as ec2,
    Stack
)

PROJECT_CODE = 'eshop_aws'


class ElasticCacheRedisStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc: ec2.Vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        security_group = ec2.SecurityGroup(
            scope=self,
            id=f"{PROJECT_CODE}_security_group",
            vpc=vpc,
        )

        private_subnets_ids = [ps.subnet_id for ps in vpc.private_subnets]
        
        print("-------------------------------------")
        for ps in private_subnets_ids:
            print(ps)
            
        print("--------------------------------------")

        # cache_subnet_group = aws_elasticache.CfnSubnetGroup(
        #     scope=self,
        #     id=f"{PROJECT_CODE}_cache_subnet_group",
        #     cache_subnet_group_name=f"{PROJECT_CODE}_cache_subnet_group",
        #     subnet_ids=private_subnets_ids,
        #     description="subnet group for redis"
        # )

        # redis_cluster = aws_elasticache.CfnCacheCluster(
        #     scope=self,
        #     id=f"{PROJECT_CODE}_redis",
        #     engine="redis",
        #     cache_node_type="cache.t2.small",
        #     num_cache_nodes=self.node.try_get_context(
        #         "redis_node_quantity"),
        #     cluster_name="eshop-aws-redis",
        #     vpc_security_group_ids=[security_group.security_group_id],
        #     cache_subnet_group_name=cache_subnet_group.ref)

        # redis_cluster.add_depends_on(cache_subnet_group)

        # cache_parameter_group_name = "default.redis5.0"

        # redis_replication = aws_elasticache.CfnReplicationGroup(self,
        #                                                         f"{PROJECT_CODE}-replication-group",
        #                                                         replication_group_description=f"{PROJECT_CODE}-replication group",
        #                                                         cache_node_type="cache.t3.micro",
        #                                                         cache_parameter_group_name=cache_parameter_group_name,
        #                                                         security_group_ids=[
        #                                                             security_group],
        #                                                         cache_subnet_group_name=cache_subnet_group.cache_subnet_group_name,
        #                                                         automatic_failover_enabled=True,
        #                                                         auto_minor_version_upgrade=True,
        #                                                         engine="redis",
        #                                                         engine_version="5.0.4",
        #                                                         # node_group_configuration
        #                                                         num_node_groups=1,  # shard  should be 3
        #                                                         replicas_per_node_group=1  # one replica
        #                                                         )

        # redis_replication.add_depends_on(cache_subnet_group)

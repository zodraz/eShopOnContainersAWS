from constructs import Construct
from aws_cdk import (
    aws_elasticache,
    aws_ec2 as ec2,
    Stack
)


class ElastiCacheRedisStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc: ec2.Vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        security_group = ec2.SecurityGroup(
            scope=self,
            id=f"RedisSecurityGroup",
            vpc=vpc,
        )

        list_subnets = self.node.try_get_context(
            "vpc_cidr_redis_subnets").split(',')

        for subnet in list_subnets:
            security_group.add_ingress_rule(
                peer=ec2.Peer.ipv4(subnet),
                description="Allow Redis connection",
                connection=ec2.Port.tcp(6379)
            )

        if self.node.try_get_context("vpc_only_public") == "False":
            subnets_ids = [ps.subnet_id for ps in vpc.private_subnets]
        else:
            subnets_ids = [ps.subnet_id for ps in vpc.public_subnets]

        cache_subnet_group = aws_elasticache.CfnSubnetGroup(
            scope=self,
            id=f"RedisCacheSubnetGroup",
            cache_subnet_group_name=f"EshopRedisCacheSubnetGroup",
            subnet_ids=subnets_ids,
            description="subnet group for redis"
        )

        if self.node.try_get_context("deploy_redis_as_replication_group") == "True":
            cache_parameter_group_name = "default.redis5.0"

            redis_replication = aws_elasticache.CfnReplicationGroup(self,
                                                                    "EshopRedis",
                                                                    replication_group_id="eshop-aws-redis",
                                                                    replication_group_description=f"eshop-aws-replication group",
                                                                    cache_node_type="cache.t3.small",
                                                                    cache_parameter_group_name=cache_parameter_group_name,
                                                                    security_group_ids=[
                                                                        security_group.security_group_id],
                                                                    cache_subnet_group_name=cache_subnet_group.cache_subnet_group_name,
                                                                    automatic_failover_enabled=True,
                                                                    auto_minor_version_upgrade=True,
                                                                    multi_az_enabled=True,
                                                                    engine="redis",
                                                                    engine_version="5.0.6",
                                                                    num_node_groups=1,
                                                                    replicas_per_node_group=self.node.try_get_context(
                                                                        "redis_node_quantity"),
                                                                    at_rest_encryption_enabled=True,
                                                                    transit_encryption_enabled=True
                                                                    )

            redis_replication.add_depends_on(cache_subnet_group)
        else:
            redis_cluster = aws_elasticache.CfnCacheCluster(
                scope=self,
                id="EshopRedis",
                engine="redis",
                cache_node_type="cache.t3.micro",
                num_cache_nodes=1,
                cluster_name="eshop-aws-redis",
                vpc_security_group_ids=[security_group.security_group_id],
                cache_subnet_group_name=cache_subnet_group.ref)

            redis_cluster.add_depends_on(cache_subnet_group)

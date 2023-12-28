"""
Purpose

Example of how to provision an EKS cluster, create the IAM Roles for Service Accounts (IRSA) mappings,
and then deploy various common cluster add-ons (AWS Load Balancer Controller, ExternalDNS, EBS & EFS CSI Drivers,
Cluster Autoscaler, AWS Managed OpenSearch and fluentbit, Metrics Server, Calico Network Policy provider,
CloudWatch Container Insights, Security Groups for Pods, Kubecost, AWS Managed Prometheus and Grafana, etc.)

NOTE: This pulls many parameters/options for what you'd like from the cdk.json context section.
Have a look there for many options you can change to customise this template for your environments/needs.
"""

from aws_cdk import (aws_ec2 as ec2, aws_eks as eks, aws_iam as iam,
                     aws_opensearchservice as opensearch, aws_logs as logs,
                     aws_certificatemanager as cm, CfnOutput,
                     RemovalPolicy, Stack, aws_route53 as route53,
                     lambda_layer_kubectl_v28)
from constructs import Construct
import yaml

from amp_custom_resource import AMPCustomResource
from eks_worker_role_statements import EksWorkerRoleStatements


class EKSClusterStack(Stack):

    def __init__(self, scope: Construct, id: str, vpc: ec2.Vpc, **kwargs) -> None:
        super().__init__(scope, id, **kwargs)

        statement = EksWorkerRoleStatements()

        # Either create a new IAM role to administrate the cluster or create a new one
        if self.node.try_get_context(
                "create_new_cluster_admin_role") == "True":
            self.cluster_admin_role = iam.Role(
                self,
                "EKSMasterRole",
                role_name='eks-admin-role',
                assumed_by=iam.CompositePrincipal(
                    iam.AccountRootPrincipal(),
                    iam.ServicePrincipal("ec2.amazonaws.com"),
                ),
            )
            cluster_admin_policy_statement_json_1 = {
                "Effect": "Allow",
                "Action": ["eks:DescribeCluster"],
                "Resource": "*",
            }
            self.cluster_admin_role.add_to_principal_policy(
                iam.PolicyStatement.from_json(
                    cluster_admin_policy_statement_json_1))

        else:
            # You'll also need to add a trust relationship to ec2.amazonaws.com to sts:AssumeRole to this as well
            self.cluster_admin_role = iam.Role.from_role_arn(
                self,
                "ClusterAdminRole",
                role_arn=self.node.try_get_context("existing_admin_role_arn"),
            )

         # EKS Node Role
        node_role = iam.Role(
            self, "EKSNodeRole", role_name='eks-node-role', assumed_by=iam.ServicePrincipal("eks.amazonaws.com")
        )
        node_role.add_managed_policy(
            iam.ManagedPolicy.from_aws_managed_policy_name("AmazonEKSClusterPolicy"))
        node_role.add_managed_policy(
            iam.ManagedPolicy.from_aws_managed_policy_name("AmazonEKSWorkerNodePolicy"))
        node_role.add_managed_policy(iam.ManagedPolicy.from_aws_managed_policy_name(
            "AmazonEC2ContainerRegistryReadOnly"))
        node_role.add_managed_policy(
            iam.ManagedPolicy.from_aws_managed_policy_name("AmazonEKSServicePolicy"))

        # Create an EKS Cluster
        if self.node.try_get_context("vpc_only_public") == "True":
            vpc_subnets = [ec2.SubnetSelection(
                subnet_type=ec2.SubnetType.PUBLIC)]
            endpoint_access = eks.EndpointAccess.PUBLIC
        else:
            vpc_subnets = [ec2.SubnetSelection(
                subnet_type=ec2.SubnetType.PRIVATE_WITH_EGRESS)]
            endpoint_access = eks.EndpointAccess.PRIVATE

        eks_cluster = eks.Cluster(
            self,
            "cluster",
            vpc=vpc,
            masters_role=self.cluster_admin_role,
            endpoint_access=endpoint_access,
            version=eks.KubernetesVersion.of(
                self.node.try_get_context("eks_version")),
            kubectl_layer=lambda_layer_kubectl_v28.KubectlV28Layer(
                self, "kubectl"),
            default_capacity=0,
            role=node_role,
            vpc_subnets=vpc_subnets,
            cluster_logging=[eks.ClusterLoggingTypes.API, eks.ClusterLoggingTypes.AUTHENTICATOR, eks.ClusterLoggingTypes.SCHEDULER])

        # Create the CF exports that let you rehydrate the Cluster object in other stack(s)
        if self.node.try_get_context("create_cluster_exports") == "True":
            # Output the EKS Cluster Name and Export it
            CfnOutput(
                self,
                "EKSClusterName",
                value=eks_cluster.cluster_name,
                description="The name of the EKS Cluster",
                export_name="EKSClusterName",
            )
            # Output the EKS Cluster OIDC Issuer and Export it
            CfnOutput(
                self,
                "EKSClusterOIDCProviderARN",
                value=eks_cluster.open_id_connect_provider.
                open_id_connect_provider_arn,
                description="The EKS Cluster's OIDC Provider ARN",
                export_name="EKSClusterOIDCProviderARN",
            )
            # Output the EKS Cluster kubectl Role ARN
            CfnOutput(
                self,
                "EKSClusterKubectlRoleARN",
                value=eks_cluster.kubectl_role.role_arn,
                description="The EKS Cluster's kubectl Role ARN",
                export_name="EKSClusterKubectlRoleARN",
            )
            # Output the EKS Cluster kubectl LambdaRole ARN
            CfnOutput(
                self,
                "EKSClusterKubectlLambdaRoleARN",
                value=eks_cluster.kubectl_lambda_role.role_arn,
                description="The EKS Cluster's kubectl Lambda Role ARN",
                export_name="EKSClusterKubectlLambdaRoleARN",
            )
            # Output the EKS Cluster SG ID
            CfnOutput(
                self,
                "EKSSGID",
                value=eks_cluster.kubectl_security_group.security_group_id,
                description="The EKS Cluster's kubectl SG ID",
                export_name="EKSSGID",
            )

            CfnOutput(
                self,
                id="EKSClusterOIDCProvider",
                value=eks_cluster.cluster_open_id_connect_issuer,
                description="The EKS Cluster's OIDC Provider",
                export_name="EKSClusterOIDCProvider",
            )

            CfnOutput(
                self,
                id="EKSClusterMasterIAMRole",
                value=eks_cluster.admin_role.role_arn,
                description="The EKS Cluster Master Admin Role ARN",
                export_name="EKSClusterMasterIAMRole",
            )

            CfnOutput(
                self,
                id="EKSClusterControlPlaneIAMRole",
                value=eks_cluster.role.role_arn,
                description="The EKS Cluster Control Plane Role ARN",
                export_name="EKSClusterControlPlaneIAMRole",
            )

        # Add a Managed Node Group
        if self.node.try_get_context("eks_deploy_managed_nodegroup") == "True":
            # If we enabled spot then use that
            if self.node.try_get_context("eks_node_spot") == "True":
                node_capacity_type = eks.CapacityType.SPOT
                list_spot_instances = self.node.try_get_context(
                    "eks_spot_node_instance_type").split(',')
                instance_types = list(
                    map(lambda x: ec2.InstanceType(x), list_spot_instances))
            # Otherwise give us OnDemand
            else:
                node_capacity_type = eks.CapacityType.ON_DEMAND
                instance_types = [ec2.InstanceType(
                    self.node.try_get_context("eks_node_instance_type"))]

            # Worker Role
            worker_role = iam.Role(self, "EKSWorkerRole", role_name='eks-worker-role',
                                   assumed_by=iam.ServicePrincipal("ec2.amazonaws.com"))
            attached_policy = ['AmazonEC2ContainerRegistryReadOnly',
                               'AmazonEKSWorkerNodePolicy', 'AmazonSSMManagedInstanceCore']
            for policy in attached_policy:
                worker_role.add_managed_policy(
                    iam.ManagedPolicy.from_aws_managed_policy_name(policy))
            worker_role.add_to_policy(statement.eks_cni())

            ssh_worker_sg = ec2.SecurityGroup(
                self, 'EksWorkerSSHSG',
                vpc=vpc,
                description='EKS SSH to worker nodes',
                security_group_name='eks-ssh'
            )
            # ssh_worker_sg.add_ingress_rule(ec2.Peer.ipv4('10.3.0.0/16'), ec2.Port.tcp(22), "SSH Access")
            ssh_worker_sg.add_ingress_rule(
                ec2.Peer.any_ipv4(), ec2.Port.tcp(22), "SSH Access")

            if self.node.try_get_context("ssh_key").strip() != "":
                ssh_key = self.node.try_get_context("ssh_key").strip()
            else:
                ec2.CfnKeyPair(self, "eshopKeyPair",
                               key_name="eshop-eks-ssh-key")
                ssh_key = "eshop-eks-ssh-key"

            eks_node_group = eks_cluster.add_nodegroup_capacity(
                "cluster-default-ng",
                capacity_type=node_capacity_type,
                desired_size=self.node.try_get_context("eks_node_quantity"),
                min_size=self.node.try_get_context("eks_node_quantity"),
                max_size=self.node.try_get_context("eks_node_max_quantity"),
                disk_size=self.node.try_get_context("eks_node_disk_size"),
                labels={'role': 'worker'},
                nodegroup_name='eks-node-group',
                node_role=worker_role,
                remote_access=eks.NodegroupRemoteAccess(
                    ssh_key_name=ssh_key, source_security_groups=[ssh_worker_sg]),
                # The default in CDK is to force upgrades through even if they violate - it is safer to not do that
                force_update=False,
                instance_types=instance_types,
                release_version=self.node.try_get_context(
                    "eks_node_ami_version"),
            )
            eks_node_group.role.add_managed_policy(
                iam.ManagedPolicy.from_aws_managed_policy_name(
                    "AmazonSSMManagedInstanceCore"))

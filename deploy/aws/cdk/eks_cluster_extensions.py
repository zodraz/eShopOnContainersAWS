"""
Purpose

Example of how to provision an EKS cluster, create the IAM Roles for Service Accounts (IRSA) mappings,
and then deploy various common cluster add-ons (AWS Load Balancer Controller, ExternalDNS, EBS & EFS CSI Drivers,
Cluster Autoscaler, AWS Managed OpenSearch and fluentbit, Metrics Server, Calico Network Policy provider,
CloudWatch Container Insights, Security Groups for Pods, Kubecost, AWS Managed Prometheus and Grafana, etc.)

NOTE: This pulls many parameters/options for what you'd like from the cdk.json context section.
Have a look there for many options you can change to customise this template for your environments/needs.
"""

from aws_cdk import (CfnJson, aws_ec2 as ec2, aws_eks as eks, aws_iam as iam,
                     aws_opensearchservice as opensearch, aws_logs as logs,
                     aws_certificatemanager as cm, CfnOutput,
                     RemovalPolicy, Stack, aws_route53 as route53,
                     lambda_layer_kubectl_v28, Fn)
from constructs import Construct
import yaml

from amp_custom_resource import AMPCustomResource
from eks_worker_role_statements import EksWorkerRoleStatements


class EKSClusterStackExtensions(Stack):

    def __init__(self, scope: Construct, id: str, vpc: ec2.Vpc, **kwargs) -> None:
        super().__init__(scope, id, **kwargs)

        eks_cluster_name = Fn.import_value("EKSClusterName")

        eks_lambda_cluster_role_arn = Fn.import_value(
            "EKSClusterKubectlLambdaRoleARN")

        eks_cluster_master_role_arn = Fn.import_value(
            "EKSClusterMasterIAMRole")

        eks_cluster_control_plane_role_arn = Fn.import_value(
            "EKSClusterControlPlaneIAMRole")

        eks_open_id_connect_provider_arn = Fn.import_value(
            "EKSClusterOIDCProviderARN")

        eks_open_id_connect_provider_url = Fn.import_value(
            "EKSClusterOIDCProvider")

        eks_cluster = eks.Cluster.from_cluster_attributes(self, "eks_cluster",
                                                          vpc=vpc,
                                                          cluster_name=eks_cluster_name,
                                                          open_id_connect_provider=iam.OpenIdConnectProvider.from_open_id_connect_provider_arn(
                                                              self, "OpenIdConnectProvider", eks_open_id_connect_provider_arn),
                                                          kubectl_lambda_role=iam.Role.from_role_arn(
                                                              self, "KubectlLambdaRole", role_arn=eks_lambda_cluster_role_arn),
                                                          kubectl_role_arn=eks_cluster_master_role_arn)

        # Create a Fargate Pod Execution Role to use with any Fargate Profiles
        # We create this explicitly to allow for logging without fargate_only_cluster=True
        fargate_pod_execution_role = iam.Role(
            self,
            "FargatePodExecutionRole",
            assumed_by=iam.ServicePrincipal("eks-fargate-pods.amazonaws.com"),
            managed_policies=[
                iam.ManagedPolicy.from_aws_managed_policy_name(
                    "AmazonEKSFargatePodExecutionRolePolicy")
            ],
        )

        def string_like(name_space, sa_name):
            string = CfnJson(
                self, f'JsonCondition{sa_name}',
                value={
                    f'{eks_open_id_connect_provider_url}:sub': f'system:serviceaccount:{name_space}:{sa_name}',
                    f'{eks_open_id_connect_provider_url}:aud': 'sts.amazonaws.com'
                }
            )
            return string

        # AWS Load Balancer Controller
        if self.node.try_get_context("deploy_aws_lb_controller") == "True":

            awslbcontroller_service_account = eks_cluster.add_service_account(
                "aws-load-balancer-controller",
                name="aws-load-balancer-controller",
                namespace="kube-system",
                annotations={
                    "eks.amazonaws.com/role-arn": "arn:aws:iam::" + self.account + ":role/eshop-lb-role"
                }
            )

            # Create the PolicyStatements to attach to the role
            # Got the required policy from https://raw.githubusercontent.com/kubernetes-sigs/aws-load-balancer-controller/main/docs/install/iam_policy.json
            awslbcontroller_policy_1 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "iam:CreateServiceLinkedRole",
                    "ec2:DescribeAccountAttributes",
                    "ec2:DescribeAddresses",
                    "ec2:DescribeAvailabilityZones",
                    "ec2:DescribeInternetGateways",
                    "ec2:DescribeVpcs",
                    "ec2:DescribeSubnets",
                    "ec2:DescribeSecurityGroups",
                    "ec2:DescribeInstances",
                    "ec2:DescribeNetworkInterfaces",
                    "ec2:DescribeTags",
                    "ec2:GetCoipPoolUsage",
                    "ec2:DescribeCoipPools",
                    "elasticloadbalancing:DescribeLoadBalancers",
                    "elasticloadbalancing:DescribeLoadBalancerAttributes",
                    "elasticloadbalancing:DescribeListeners",
                    "elasticloadbalancing:DescribeListenerCertificates",
                    "elasticloadbalancing:DescribeSSLPolicies",
                    "elasticloadbalancing:DescribeRules",
                    "elasticloadbalancing:DescribeTargetGroups",
                    "elasticloadbalancing:DescribeTargetGroupAttributes",
                    "elasticloadbalancing:DescribeTargetHealth",
                    "elasticloadbalancing:DescribeTags",
                ],
                "Resource":
                "*",
            })
            awslbcontroller_policy_2 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "cognito-idp:DescribeUserPoolClient",
                    "acm:ListCertificates",
                    "acm:DescribeCertificate",
                    "iam:ListServerCertificates",
                    "iam:GetServerCertificate",
                    "waf-regional:GetWebACL",
                    "waf-regional:GetWebACLForResource",
                    "waf-regional:AssociateWebACL",
                    "waf-regional:DisassociateWebACL",
                    "wafv2:GetWebACL",
                    "wafv2:GetWebACLForResource",
                    "wafv2:AssociateWebACL",
                    "wafv2:DisassociateWebACL",
                    "shield:GetSubscriptionState",
                    "shield:DescribeProtection",
                    "shield:CreateProtection",
                    "shield:DeleteProtection",
                ],
                "Resource":
                "*",
            })
            awslbcontroller_policy_3 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "ec2:AuthorizeSecurityGroupIngress",
                    "ec2:RevokeSecurityGroupIngress",
                ],
                "Resource":
                "*",
            })
            awslbcontroller_policy_4 = iam.PolicyStatement.from_json({
                "Effect": "Allow",
                "Action": ["ec2:CreateSecurityGroup"],
                "Resource": "*",
            })
            awslbcontroller_policy_5 = iam.PolicyStatement.from_json({
                "Effect": "Allow",
                "Action": ["ec2:CreateTags"],
                "Resource": "arn:aws:ec2:*:*:security-group/*",
                "Condition": {
                            "StringEquals": {
                                "ec2:CreateAction": "CreateSecurityGroup"
                            },
                    "Null": {
                                "aws:RequestTag/elbv2.k8s.aws/cluster": "false"
                            },
                },
            })
            awslbcontroller_policy_6 = iam.PolicyStatement.from_json({
                "Effect": "Allow",
                "Action": ["ec2:CreateTags", "ec2:DeleteTags"],
                "Resource": "arn:aws:ec2:*:*:security-group/*",
                "Condition": {
                            "Null": {
                                "aws:RequestTag/elbv2.k8s.aws/cluster": "true",
                                "aws:ResourceTag/elbv2.k8s.aws/cluster":
                                "false",
                            }
                },
            })
            awslbcontroller_policy_7 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "ec2:AuthorizeSecurityGroupIngress",
                    "ec2:RevokeSecurityGroupIngress",
                    "ec2:DeleteSecurityGroup",
                ],
                "Resource":
                "*",
                "Condition": {
                    "Null": {
                        "aws:ResourceTag/elbv2.k8s.aws/cluster":
                        "false"
                    }
                },
            })
            awslbcontroller_policy_8 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "elasticloadbalancing:CreateLoadBalancer",
                    "elasticloadbalancing:CreateTargetGroup",
                ],
                "Resource":
                "*",
                "Condition": {
                    "Null": {
                        "aws:RequestTag/elbv2.k8s.aws/cluster": "false"
                    }
                },
            })
            awslbcontroller_policy_9 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "elasticloadbalancing:CreateListener",
                    "elasticloadbalancing:DeleteListener",
                    "elasticloadbalancing:CreateRule",
                    "elasticloadbalancing:DeleteRule",
                ],
                "Resource":
                "*",
            })
            awslbcontroller_policy_10 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "elasticloadbalancing:AddTags",
                    "elasticloadbalancing:RemoveTags",
                ],
                "Resource": [
                    "arn:aws:elasticloadbalancing:*:*:targetgroup/*/*",
                    "arn:aws:elasticloadbalancing:*:*:loadbalancer/net/*/*",
                    "arn:aws:elasticloadbalancing:*:*:loadbalancer/app/*/*",
                ],
                # "Condition": {
                #     "Null": {
                #         "aws:RequestTag/elbv2.k8s.aws/cluster": "true",
                #         "aws:ResourceTag/elbv2.k8s.aws/cluster":
                #         "false",
                #     }
                # },
            })
            awslbcontroller_policy_11 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "elasticloadbalancing:AddTags",
                    "elasticloadbalancing:RemoveTags",
                ],
                "Resource": [
                    "arn:aws:elasticloadbalancing:*:*:listener/net/*/*/*",
                    "arn:aws:elasticloadbalancing:*:*:listener/app/*/*/*",
                    "arn:aws:elasticloadbalancing:*:*:listener-rule/net/*/*/*",
                    "arn:aws:elasticloadbalancing:*:*:listener-rule/app/*/*/*",
                ],
            })
            awslbcontroller_policy_12 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "elasticloadbalancing:ModifyLoadBalancerAttributes",
                    "elasticloadbalancing:SetIpAddressType",
                    "elasticloadbalancing:SetSecurityGroups",
                    "elasticloadbalancing:SetSubnets",
                    "elasticloadbalancing:DeleteLoadBalancer",
                    "elasticloadbalancing:ModifyTargetGroup",
                    "elasticloadbalancing:ModifyTargetGroupAttributes",
                    "elasticloadbalancing:DeleteTargetGroup",
                ],
                "Resource":
                "*",
                "Condition": {
                    "Null": {
                        "aws:ResourceTag/elbv2.k8s.aws/cluster":
                        "false"
                    }
                },
            })
            awslbcontroller_policy_13 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "elasticloadbalancing:RegisterTargets",
                    "elasticloadbalancing:DeregisterTargets",
                ],
                "Resource":
                "arn:aws:elasticloadbalancing:*:*:targetgroup/*/*",
            })
            awslbcontroller_policy_14 = iam.PolicyStatement.from_json({
                "Effect":
                "Allow",
                "Action": [
                    "elasticloadbalancing:SetWebAcl",
                    "elasticloadbalancing:ModifyListener",
                    "elasticloadbalancing:AddListenerCertificates",
                    "elasticloadbalancing:RemoveListenerCertificates",
                    "elasticloadbalancing:ModifyRule",
                ],
                "Resource":
                "*",
            })

            awslbcontroller_role = iam.Role(
                self,
                'AwsLbControllerRole',
                role_name="eshop-lb-role",
                # for Role's Trust relationships
                assumed_by=iam.FederatedPrincipal(
                    federated=eks_cluster.open_id_connect_provider.
                    open_id_connect_provider_arn,
                    conditions={'StringEquals': string_like(
                        'kube-system', 'aws-load-balancer-controller')},
                    assume_role_action='sts:AssumeRoleWithWebIdentity'
                )
            )

            # Attach the necessary permissions
            # awslbcontroller_policy = iam.Policy(
            #     self,
            #     "awslbcontrollerpolicy",
            #     document=iam.PolicyDocument.from_json(
            #         awslbcontroller_policy_14)
            # )
            # awslbcontroller_service_account.add_to_principal_policy(
            #     awslbcontroller_policy)

            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_1)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_2)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_3)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_4)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_5)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_6)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_7)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_8)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_9)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_10)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_11)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_12)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_13)
            awslbcontroller_role.add_to_principal_policy(
                awslbcontroller_policy_14)

            # Deploy the AWS Load Balancer Controller from the AWS Helm Chart
            awslbcontroller_chart = eks_cluster.add_helm_chart(
                "aws-load-balancer-controller",
                chart="aws-load-balancer-controller",
                version="1.4.8",
                release="awslbcontroller",
                repository="https://aws.github.io/eks-charts",
                namespace="kube-system",
                values={
                    "clusterName": eks_cluster.cluster_name,
                    "ingressClass": "aws-alb",
                    "region": self.region,
                    "vpcId": vpc.vpc_id,
                    "serviceAccount": {
                        "create": False,
                        "name": "aws-load-balancer-controller",
                    },
                    "ingressClassParams": {
                        "spec": {
                            "scheme": "internet-facing",
                            "group": {
                                "name": "eks-alb-ingress"
                            },
                            "loadBalancerAttributes": [
                                {
                                    "key": "deletion_protection.enabled",
                                    "value": "true"
                                },
                                {
                                    "key": "idle_timeout.timeout_seconds",
                                    "value": "120"
                                },
                                {
                                    "key": "routing.http.drop_invalid_header_fields.enabled",
                                    "value": "true"
                                },
                                {
                                    "key": "routing.http2.enabled",
                                    "value": "true"
                                },
                                {
                                    "key": "routing.http.preserve_host_header.enabled",
                                    "value": "true"
                                }
                            ]
                        }
                    },
                    "replicaCount": 2,
                },
            )
            awslbcontroller_chart.node.add_dependency(
                awslbcontroller_service_account)

        # External DNS Controller
        if self.node.try_get_context("deploy_external_dns") == "True":
            externaldns_service_account = eks_cluster.add_service_account(
                "external-dns", name="external-dns", namespace="kube-system")

            zone = route53.HostedZone.from_lookup(
                self, 'HostedZone', domain_name=self.node.try_get_context("dns_domain"))

            # Create the PolicyStatements to attach to the role
            # See https://github.com/kubernetes-sigs/external-dns/blob/master/docs/tutorials/aws.md#iam-policy
            # NOTE that this will give External DNS access to all Route53 zones
            # For production you'll likely want to replace 'Resource *' with specific resources
            externaldns_policy_statement_json_1 = {
                "Effect": "Allow",
                "Action": ["route53:ChangeResourceRecordSets"],
                "Resource": ["arn:aws:route53:::hostedzone/*"],
            }
            externaldns_policy_statement_json_2 = {
                "Effect":
                "Allow",
                "Action":
                ["route53:ListHostedZones", "route53:ListResourceRecordSets"],
                "Resource": ["*"],
            }

            # Attach the necessary permissions
            externaldns_service_account.add_to_principal_policy(
                iam.PolicyStatement.from_json(
                    externaldns_policy_statement_json_1))
            externaldns_service_account.add_to_principal_policy(
                iam.PolicyStatement.from_json(
                    externaldns_policy_statement_json_2))

            # Deploy External DNS from the bitnami Helm chart
            # For more info see https://github.com/bitnami/charts/tree/master/bitnami/external-dns
            externaldns_chart = eks_cluster.add_helm_chart(
                "external-dns",
                chart="external-dns",
                version="6.13.0",
                release="externaldns",
                repository="https://charts.bitnami.com/bitnami",
                namespace="kube-system",
                values={
                    "provider": "aws",
                    "domain-filter":  self.node.try_get_context("dns_domain"),
                    "txt-owner-id": zone.hosted_zone_id,
                    "aws": {
                        "region": self.region
                    },
                    "serviceAccount": {
                        "create": False,
                        "name": "external-dns"
                    },
                    "podSecurityContext": {
                        "fsGroup": 65534
                    },
                    "replicas": 2,
                },
            )
            externaldns_chart.node.add_dependency(externaldns_service_account)

        # Cluster Autoscaler
        if (self.node.try_get_context("deploy_cluster_autoscaler") == "True"
                and self.node.try_get_context("fargate_only_cluster")
                == "False"):
            clusterautoscaler_service_account = eks_cluster.add_service_account(
                "clusterautoscaler",
                name="clusterautoscaler",
                namespace="kube-system")

            # Create the PolicyStatements to attach to the role
            clusterautoscaler_policy_statement_json_1 = {
                "Effect":
                "Allow",
                "Action": [
                    "autoscaling:DescribeAutoScalingGroups",
                    "autoscaling:DescribeAutoScalingInstances",
                    "autoscaling:DescribeLaunchConfigurations",
                    "autoscaling:DescribeTags",
                    "autoscaling:SetDesiredCapacity",
                    "autoscaling:TerminateInstanceInAutoScalingGroup",
                ],
                "Resource":
                "*",
            }

            # Attach the necessary permissions
            clusterautoscaler_service_account.add_to_principal_policy(
                iam.PolicyStatement.from_json(
                    clusterautoscaler_policy_statement_json_1))

            # Install the Cluster Autoscaler
            # For more info see https://github.com/kubernetes/autoscaler
            clusterautoscaler_chart = eks_cluster.add_helm_chart(
                "cluster-autoscaler",
                chart="cluster-autoscaler",
                version="9.34.0",
                release="clusterautoscaler",
                repository="https://kubernetes.github.io/autoscaler",
                namespace="kube-system",
                values={
                    "autoDiscovery": {
                        "clusterName": eks_cluster.cluster_name
                    },
                    "awsRegion": self.region,
                    "rbac": {
                        "serviceAccount": {
                            "create": False,
                            "name": "clusterautoscaler"
                        }
                    },
                    "replicaCount": 2,
                    "extraArgs": {
                        "skip-nodes-with-system-pods": False,
                        "balance-similar-node-groups": True,
                    },
                },
            )
            clusterautoscaler_chart.node.add_dependency(
                clusterautoscaler_service_account)

        # Amazon OpenSearch and a fluent-bit to ship our container logs there
        if self.node.try_get_context("deploy_managed_opensearch") == "True":
            # Create a new OpenSearch Domain
            # NOTE: I changed this to a removal_policy of DESTROY to help cleanup while I was
            # developing/iterating on the project. If you comment out that line it defaults to keeping
            # the Domain upon deletion of the CloudFormation stack so you won't lose your log data

            os_access_policy_statement_json_1 = {
                "Effect": "Allow",
                "Action": "es:*",
                "Principal": {
                    "AWS": "*"
                },
                "Resource": "*",
            }

            # Create SecurityGroup for OpenSearch
            os_security_group = ec2.SecurityGroup(self,
                                                  "OpenSearchSecurityGroup",
                                                  vpc=vpc,
                                                  allow_all_outbound=True)
            # Add a rule to allow our new SG to talk to the EKS control plane
            eks_cluster.cluster_security_group.add_ingress_rule(
                os_security_group, ec2.Port.all_traffic())
            # Add a rule to allow the EKS control plane to talk to our new SG
            os_security_group.add_ingress_rule(
                eks_cluster.cluster_security_group, ec2.Port.all_traffic())

            # The capacity in Nodes and Volume Size/Type for the AWS OpenSearch
            os_capacity = opensearch.CapacityConfig(
                data_nodes=self.node.try_get_context(
                    "opensearch_data_nodes"),
                data_node_instance_type=self.node.try_get_context(
                    "opensearch_data_node_instance_type"),
                master_nodes=self.node.try_get_context(
                    "opensearch_master_nodes"),
                master_node_instance_type=self.node.try_get_context(
                    "opensearch_master_node_instance_type"),
            )
            os_ebs = opensearch.EbsOptions(
                enabled=True,
                volume_type=ec2.EbsDeviceVolumeType.GP2,
                volume_size=self.node.try_get_context(
                    "opensearch_ebs_volume_size"),
            )

            # Note that this AWS OpenSearch domain is optimised for cost rather than availability
            # and defaults to one node in a single availability zone
            os_domain = opensearch.Domain(
                self,
                "OSDomain",
                removal_policy=RemovalPolicy.DESTROY,
                # https://docs.aws.amazon.com/cdk/api/latest/docs/@aws-cdk_aws-opensearchservice.EngineVersion.html
                version=opensearch.EngineVersion.OPENSEARCH_1_0,
                vpc=vpc,
                vpc_subnets=[
                    ec2.SubnetSelection(subnets=[vpc.private_subnets[0]])
                ],
                security_groups=[os_security_group],
                capacity=os_capacity,
                ebs=os_ebs,
                access_policies=[
                    iam.PolicyStatement.from_json(
                        os_access_policy_statement_json_1)
                ],
            )

            # Output the OpenSearch Dashboards address in our CloudFormation Stack
            CfnOutput(
                self,
                "OpenSearchDashboardsAddress",
                value="https://" + os_domain.domain_endpoint + "/_dashboards/",
                description="Private endpoint for this EKS environment's OpenSearch to consume the logs",
            )

            # Create the Service Account
            fluentbit_service_account = eks_cluster.add_service_account(
                "fluentbit", name="fluentbit", namespace="kube-system")

            fluentbit_policy_statement_json_1 = {
                "Effect": "Allow",
                "Action": ["es:*"],
                "Resource": [os_domain.domain_arn],
            }

            # Add the policies to the service account
            fluentbit_service_account.add_to_principal_policy(
                iam.PolicyStatement.from_json(
                    fluentbit_policy_statement_json_1))
            os_domain.grant_write(fluentbit_service_account)

            # For more info check out https://github.com/fluent/helm-charts/tree/main/charts/fluent-bit
            fluentbit_chart = eks_cluster.add_helm_chart(
                "fluentbit",
                chart="fluent-bit",
                version="0.19.1",
                release="fluent-bit",
                repository="https://fluent.github.io/helm-charts",
                namespace="kube-system",
                values={
                    "serviceAccount": {
                        "create": False,
                        "name": "fluentbit"
                    },
                    "config": {
                        "outputs":
                        "[OUTPUT]\n    Name            es\n    Match           *\n"
                        "    AWS_Region      " + self.region +
                        "\n    AWS_Auth        On\n"
                        "    Host            " + os_domain.domain_endpoint +
                        "\n    Port            443\n"
                        "    TLS             On\n    Replace_Dots    On\n    Logstash_Format    On"
                    },
                },
            )
            fluentbit_chart.node.add_dependency(fluentbit_service_account)

        # Metrics Server (required for the Horizontal Pod Autoscaler (HPA))
        if self.node.try_get_context("deploy_metrics_server") == "True":
            # For more info see https://github.com/bitnami/charts/tree/master/bitnami/metrics-server
            metricsserver_chart = eks_cluster.add_helm_chart(
                "metrics-server",
                chart="metrics-server",
                version="6.2.6",
                release="metricsserver",
                repository="https://charts.bitnami.com/bitnami",
                namespace="kube-system",
                values={
                    "replicas": 2,
                    "apiService": {
                        "create": True
                    }
                },
            )

        # Bastion Instance
        if self.node.try_get_context("deploy_bastion") == "True":
            # Create an Instance Profile for our Admin Role to assume w/EC2
            cluster_admin_role_instance_profile = iam.CfnInstanceProfile(
                self,
                "ClusterAdminRoleInstanceProfile",
                roles=[self.cluster_admin_role.role_name],
            )

            # Another way into our Bastion is via Systems Manager Session Manager
            if self.node.try_get_context(
                    "create_new_cluster_admin_role") == "True":
                self.cluster_admin_role.add_managed_policy(
                    iam.ManagedPolicy.from_aws_managed_policy_name(
                        "AmazonSSMManagedInstanceCore"))

            # Create Bastion
            # Get Latest Amazon Linux AMI
            amzn_linux = ec2.MachineImage.latest_amazon_linux(
                generation=ec2.AmazonLinuxGeneration.AMAZON_LINUX_2,
                edition=ec2.AmazonLinuxEdition.STANDARD,
                virtualization=ec2.AmazonLinuxVirt.HVM,
                storage=ec2.AmazonLinuxStorage.GENERAL_PURPOSE,
            )

            # Create SecurityGroup for bastion
            bastion_security_group = ec2.SecurityGroup(self,
                                                       "BastionSecurityGroup",
                                                       vpc=vpc,
                                                       allow_all_outbound=True)

            # Add a rule to allow our new SG to talk to the EKS control plane
            eks_cluster.cluster_security_group.add_ingress_rule(
                bastion_security_group, ec2.Port.all_traffic())

            # Create our EC2 instance for bastion
            bastion_instance = ec2.Instance(
                self,
                "BastionInstance",
                instance_type=ec2.InstanceType(
                    self.node.try_get_context("bastion_node_type")),
                machine_image=amzn_linux,
                role=self.cluster_admin_role,
                vpc=vpc,
                vpc_subnets=ec2.SubnetSelection(
                    subnet_type=ec2.SubnetType.PUBLIC),
                security_group=bastion_security_group,
                block_devices=[
                    ec2.BlockDevice(
                        device_name="/dev/xvda",
                        volume=ec2.BlockDeviceVolume.ebs(
                            self.node.try_get_context("bastion_disk_size")),
                    )
                ],
            )

            # Set up our kubectl and fluxctl
            bastion_instance.user_data.add_commands(
                "curl -o kubectl https://amazon-eks.s3.us-west-2.amazonaws.com/1.21.2/2021-07-05/bin/linux/amd64/kubectl"
            )
            bastion_instance.user_data.add_commands("chmod +x ./kubectl")
            bastion_instance.user_data.add_commands(
                "mv ./kubectl /usr/bin")
            bastion_instance.user_data.add_commands(
                "curl -s https://raw.githubusercontent.com/helm/helm/master/scripts/get-helm-3 | bash -"
            )
            bastion_instance.user_data.add_commands(
                "curl -s https://fluxcd.io/install.sh | bash -")
            bastion_instance.user_data.add_commands(
                "curl --silent --location https://rpm.nodesource.com/setup_14.x | bash -"
            )
            bastion_instance.user_data.add_commands(
                "yum install nodejs git -y")
            bastion_instance.user_data.add_commands(
                'su -c "aws eks update-kubeconfig --name ' +
                eks_cluster.cluster_name + " --region " + self.region +
                '" ssm-user')

            # Wait to deploy Bastion until cluster is up and we're deploying manifests/charts to it
            # This could be any of the charts/manifests I just picked this one as almost everybody will want it
            bastion_instance.node.add_dependency(metricsserver_chart)

        # Client VPN
        if self.node.try_get_context("deploy_client_vpn") == "True":
            # Create and upload your client and server certs as per https://docs.aws.amazon.com/vpn/latest/clientvpn-admin/client-authentication.html#mutual
            # And then put the ARNs for them into the items below
            client_cert = cm.Certificate.from_certificate_arn(
                self,
                "ClientCert",
                certificate_arn=self.node.try_get_context(
                    "vpn_client_certificate_arn"),
            )
            server_cert = cm.Certificate.from_certificate_arn(
                self,
                "ServerCert",
                certificate_arn=self.node.try_get_context(
                    "vpn_server_certificate_arn"),
            )

            # Create SecurityGroup for VPN
            vpn_security_group = ec2.SecurityGroup(self,
                                                   "VPNSecurityGroup",
                                                   vpc=vpc,
                                                   allow_all_outbound=True)
            # Add a rule to allow our new SG to talk to the EKS control plane
            eks_cluster.cluster_security_group.add_ingress_rule(
                vpn_security_group, ec2.Port.all_traffic())

            if self.node.try_get_context(
                    "deploy_managed_opensearch") == "True":
                # Add a rule to allow our new SG to talk to Elastic
                os_security_group.add_ingress_rule(vpn_security_group,
                                                   ec2.Port.all_traffic())

            # Create CloudWatch Log Group and Stream and keep the logs for 1 month
            log_group = logs.LogGroup(self,
                                      "VPNLogGroup",
                                      retention=logs.RetentionDays.ONE_MONTH)
            log_stream = log_group.add_stream("VPNLogStream")

            endpoint = ec2.CfnClientVpnEndpoint(
                self,
                "VPNEndpoint",
                description="EKS Client VPN",
                authentication_options=[{
                    "type": "certificate-authentication",
                    "mutualAuthentication": {
                        "clientRootCertificateChainArn":
                        client_cert.certificate_arn
                    },
                }],
                client_cidr_block=self.node.try_get_context(
                    "vpn_client_cidr_block"),
                server_certificate_arn=server_cert.certificate_arn,
                connection_log_options={
                    "enabled": True,
                    "cloudwatchLogGroup": log_group.log_group_name,
                    "cloudwatchLogStream": log_stream.log_stream_name,
                },
                split_tunnel=True,
                security_group_ids=[vpn_security_group.security_group_id],
                vpc_id=vpc.vpc_id,
            )

            ec2.CfnClientVpnAuthorizationRule(
                self,
                "ClientVpnAuthRule",
                client_vpn_endpoint_id=endpoint.ref,
                target_network_cidr=vpc.vpc_cidr_block,
                authorize_all_groups=True,
                description="Authorize the Client VPN access to our VPC CIDR",
            )

            ec2.CfnClientVpnTargetNetworkAssociation(
                self,
                "ClientVpnNetworkAssociation",
                client_vpn_endpoint_id=endpoint.ref,
                subnet_id=vpc.private_subnets[0].subnet_id,
            )

        # CloudWatch Container Insights - Metrics
        if (self.node.try_get_context("deploy_cloudwatch_container_insights_metrics") == "True"):
            # For more info see https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/Container-Insights-setup-metrics.html

            # Create the Service Account
            cw_container_insights_sa = eks_cluster.add_service_account(
                "cloudwatch-agent",
                name="cloudwatch-agent",
                namespace="kube-system")
            cw_container_insights_sa.role.add_managed_policy(
                iam.ManagedPolicy.from_aws_managed_policy_name(
                    "CloudWatchAgentServerPolicy"))

            # Set up the settings ConfigMap
            cw_container_insights_configmap = eks_cluster.add_manifest(
                "CWAgentConfigMap",
                {
                    "apiVersion": "v1",
                    "data": {
                        "cwagentconfig.json":
                        '{\n  "logs": {\n    "metrics_collected": {\n      "kubernetes": {\n        "cluster_name": "'
                        + eks_cluster.cluster_name +
                        '",\n        "metrics_collection_interval": 60\n      }\n    },\n    "force_flush_interval": 5\n  }\n}\n'
                    },
                    "kind": "ConfigMap",
                    "metadata": {
                        "name": "cwagentconfig",
                        "namespace": "kube-system"
                    },
                },
            )

            # Import cloudwatch-agent.yaml to a list of dictionaries and submit them as a manifest to EKS
            # Read the YAML file
            cw_agent_yaml_file = open("cloudwatch-agent.yaml", "r")
            cw_agent_yaml = list(
                yaml.load_all(cw_agent_yaml_file, Loader=yaml.FullLoader))
            cw_agent_yaml_file.close()
            loop_iteration = 0
            for value in cw_agent_yaml:
                # print(value)
                loop_iteration = loop_iteration + 1
                manifest_id = "CWAgent" + str(loop_iteration)
                eks_cluster.add_manifest(manifest_id, value)

        # CloudWatch Container Insights - Logs
        if (self.node.try_get_context(
                "deploy_cloudwatch_container_insights_logs") == "True"):
            # Create the Service Account
            fluentbit_cw_service_account = eks_cluster.add_service_account(
                "fluentbit-cw", name="fluentbit-cw", namespace="kube-system")

            fluentbit_cw_policy_statement_json_1 = {
                "Effect":
                "Allow",
                "Action": [
                    "logs:PutLogEvents",
                    "logs:DescribeLogStreams",
                    "logs:DescribeLogGroups",
                    "logs:CreateLogStream",
                    "logs:CreateLogGroup",
                    "logs:PutRetentionPolicy",
                ],
                "Resource": ["*"],
            }

            # Add the policies to the service account
            fluentbit_cw_service_account.add_to_principal_policy(
                iam.PolicyStatement.from_json(
                    fluentbit_cw_policy_statement_json_1))

            # For more info check out https://github.com/fluent/helm-charts/tree/main/charts/fluent-bit
            # Matched the config suggsted for Fargate for consistency https://docs.aws.amazon.com/eks/latest/userguide/fargate-logging.html
            fluentbit_chart_cw = eks_cluster.add_helm_chart(
                "fluentbit-cw",
                chart="fluent-bit",
                version="0.19.1",
                release="fluent-bit-cw",
                repository="https://fluent.github.io/helm-charts",
                namespace="kube-system",
                values={
                    "serviceAccount": {
                        "create": False,
                        "name": "fluentbit-cw"
                    },
                    "config": {
                        "outputs":
                        "[OUTPUT]\n    Name cloudwatch_logs\n    Match   *\n    region "
                        + self.region +
                        "\n    log_group_name fluent-bit-cloudwatch\n    log_stream_prefix from-fluent-bit-\n    auto_create_group true\n    log_retention_days "
                        + str(
                            self.node.try_get_context(
                                "cloudwatch_container_insights_logs_retention_days"
                            )) + "\n",
                        "customParsers":
                        "[PARSER]\n    Name crio\n    Format Regex\n    Regex ^(?<time>[^ ]+) (?<stream>stdout|stderr) (?<logtag>P|F) (?<log>.*)$\n    Time_Key    time\n    Time_Format %Y-%m-%dT%H:%M:%S.%L%z\n",
                        "filters":
                        "[FILTER]\n    Name parser\n    Match *\n    Key_name log\n    Parser crio\n    Reserve_Data On\n    Preserve_Key On\n",
                    },
                },
            )
            fluentbit_chart_cw.node.add_dependency(
                fluentbit_cw_service_account)

        # Kubecost
        if (self.node.try_get_context("deploy_kubecost") == "True" and
                self.node.try_get_context("fargate_only_cluster") == "False"):
            # For more information see https://www.kubecost.com/install#show-instructions
            # And https://github.com/kubecost/cost-analyzer-helm-chart/tree/master

            # If we're deploying Prometheus then we don't need the node exporter
            if self.node.try_get_context("deploy_amp") == "True":
                kubecost_values = {
                    "kubecostToken":
                    self.node.try_get_context("kubecost_token"),
                    "prometheus": {
                        "nodeExporter": {
                            "enabled": False
                        },
                        "kube-state-metrics": {
                            "disabled": True
                        },
                        "serviceAccounts": {
                            "nodeExporter": {
                                "create": False
                            }
                        },
                    },
                }
            else:
                kubecost_values = {
                    "kubecostToken":
                    self.node.try_get_context("kubecost_token")
                }

            # Deploy the Helm Chart
            kubecost_chart = eks_cluster.add_helm_chart(
                "kubecost",
                chart="cost-analyzer",
                version="1.100.2",
                repository="https://kubecost.github.io/cost-analyzer/",
                namespace="kube-system",
                release="kubecost",
                values=kubecost_values,
            )

            # Deploy an internal NLB
            kubecostnlb_manifest = eks_cluster.add_manifest(
                "KubecostNLB",
                {
                    "kind": "Service",
                    "apiVersion": "v1",
                    "metadata": {
                        "name": "kubecost-nlb",
                        "namespace": "kube-system",
                        "annotations": {
                            "service.beta.kubernetes.io/aws-load-balancer-type":
                            "nlb-ip",
                            "service.beta.kubernetes.io/aws-load-balancer-internal":
                            "true",
                        },
                    },
                    "spec": {
                        "ports": [{
                            "name": "service",
                            "protocol": "TCP",
                            "port": 80,
                            "targetPort": 9090,
                        }],
                        "selector": {
                            "app.kubernetes.io/name": "cost-analyzer"
                        },
                        "type":
                        "LoadBalancer",
                    },
                },
            )
            kubecostnlb_manifest.node.add_dependency(kubecost_chart)

        # Amazon Managed Prometheus (AMP)
        if self.node.try_get_context("deploy_amp") == "True":
            # For more information see https://aws.amazon.com/blogs/mt/getting-started-amazon-managed-service-for-prometheus/

            # Use our AMPCustomResource to provision/deprovision the AMP
            # TODO remove this and use the proper CDK construct when it becomes available
            amp_workspace_id = AMPCustomResource(
                self, "AMPCustomResource").workspace_id
            # Output the AMP Workspace ID and Export it
            CfnOutput(
                self,
                "AMPWorkspaceID",
                value=amp_workspace_id,
                description="The ID of the AMP Workspace",
                export_name="AMPWorkspaceID",
            )

            # Create IRSA mapping
            amp_sa = eks_cluster.add_service_account(
                "amp-sa",
                name="amp-iamproxy-service-account",
                namespace="kube-system")

            # Associate the IAM Policy
            amp_policy_statement_json_1 = {
                "Effect":
                "Allow",
                "Action": [
                    "aps:RemoteWrite",
                    "aps:QueryMetrics",
                    "aps:GetSeries",
                    "aps:GetLabels",
                    "aps:GetMetricMetadata",
                ],
                "Resource": ["*"],
            }
            amp_sa.add_to_principal_policy(
                iam.PolicyStatement.from_json(amp_policy_statement_json_1))

            # Install Prometheus with a low 1 hour local retention to ship the metrics to the AMP
            # For more information see https://github.com/prometheus-community/helm-charts/tree/main/charts/prometheus
            # NOTE Changed this to not use EBS PersistentVolumes so it'll work with Fargate Only Clusters
            # This should be acceptable as the metrics are immediatly streamed to the AMP
            amp_prometheus_chart = eks_cluster.add_helm_chart(
                "prometheus-chart",
                chart="prometheus",
                version="25.8.2",
                release="prometheus-for-amp",
                repository="https://prometheus-community.github.io/helm-charts",
                namespace="kube-system",
                values={
                    "serviceAccounts": {
                        "server": {
                            "annotations": {
                                "eks.amazonaws.com/role-arn":
                                amp_sa.role.role_arn,
                            },
                            "name": "amp-iamproxy-service-account",
                            "create": False,
                        },
                        "alertmanager": {
                            "create": False
                        },
                        "pushgateway": {
                            "create": False
                        },
                    },
                    "server": {
                        "resources": {
                            "limits": {
                                "cpu": 1,
                                "memory": "1Gi"
                            }
                        },
                        "persistentVolume": {
                            "enabled": False
                        },
                        "remoteWrite": [{
                            "queue_config": {
                                "max_samples_per_send": 1000,
                                "max_shards": 200,
                                "capacity": 2500,
                            },
                            "url":
                            "https://aps-workspaces." + self.region +
                            ".amazonaws.com/workspaces/" + amp_workspace_id +
                            "/api/v1/remote_write",
                            "sigv4": {
                                "region": self.region
                            },
                        }],
                        "retention":
                        "1h",
                    },
                    "alertmanager": {
                        "enabled": False
                    },
                    "pushgateway": {
                        "enabled": False
                    }
                },
            )
            amp_prometheus_chart.node.add_dependency(amp_sa)

        # Self-Managed Grafana for AMP
        if self.node.try_get_context("deploy_grafana_for_amp") == "True":
            # Install a self-managed Grafana to visualise the AMP metrics
            # NOTE You likely want to use the AWS Managed Grafana (AMG) in production
            # We are using this as AMG requires SSO/SAML and is harder to include in the template
            # NOTE We are not enabling PersistentVolumes to allow this to run in Fargate making this immutable
            # Any changes to which dashboards to use should be deployed via the ConfigMaps in order to persist
            # https://github.com/grafana/helm-charts/tree/main/charts/grafana#sidecar-for-dashboards
            # For more information see https://github.com/grafana/helm-charts/tree/main/charts/grafana
            amp_grafana_chart = eks_cluster.add_helm_chart(
                "amp-grafana-chart",
                chart="grafana",
                version="7.0.17",
                release="grafana-for-amp",
                repository="https://grafana.github.io/helm-charts",
                namespace="kube-system",
                values={
                    "serviceAccount": {
                        "name": "amp-iamproxy-service-account",
                        "annotations": {
                            "eks.amazonaws.com/role-arn": amp_sa.role.role_arn
                        },
                        "create": False,
                    },
                    "grafana.ini": {
                        "auth": {
                            "sigv4_auth_enabled": True
                        }
                    },
                    "service": {
                        "type": "LoadBalancer",
                        "annotations": {
                            "service.beta.kubernetes.io/aws-load-balancer-type":
                            "nlb-ip",
                            "service.beta.kubernetes.io/aws-load-balancer-internal":
                            "true",
                        },
                    },
                    "datasources": {
                        "datasources.yaml": {
                            "apiVersion":
                            1,
                            "datasources": [
                                {
                                    "name": "Prometheus",
                                    "type": "prometheus",
                                    "access": "proxy",
                                    "url": "https://aps-workspaces." + self.region +
                                    ".amazonaws.com/workspaces/" + amp_workspace_id,
                                    "isDefault": True,
                                    "editable": True,
                                    "jsonData": {
                                        "httpMethod": "POST",
                                        "sigV4Auth": True,
                                        "sigV4AuthType": "default",
                                        "sigV4Region": self.region,
                                    },
                                },
                                {
                                    "name": "Loki",
                                    "type": "loki",
                                    "access": "proxy",
                                    "url": "http://loki.default.svc.cluster.local:3100",
                                },
                                {
                                    "name": "Jaeger",
                                    "type": "jaeger",
                                    "uid": "my_jaeger",
                                    "access": "proxy",
                                    "url": "http://jaeger.default.svc.cluster.local:16686",
                                }
                            ],
                        }
                    },
                    "sidecar": {
                        "dashboards": {
                            "enabled": True,
                            "label": "grafana_dashboard"
                        }
                    },
                },
            )
            amp_grafana_chart.node.add_dependency(amp_prometheus_chart)

            # Dashboards for Grafana from the grafana-dashboards.yaml file
            grafana_dashboards_yaml_file = open(
                "grafana-dashboards.yaml", "r")
            grafana_dashboards_yaml = list(
                yaml.load_all(grafana_dashboards_yaml_file,
                              Loader=yaml.FullLoader))
            grafana_dashboards_yaml_file.close()
            loop_iteration = 0
            for value in grafana_dashboards_yaml:
                # print(value)
                loop_iteration = loop_iteration + 1
                manifest_id = "GrafanaDashboard" + str(loop_iteration)
                grafana_dashboard_manifest = eks_cluster.add_manifest(
                    manifest_id, value)

        if self.node.try_get_context("deploy_loki") == "True":

            # Create IRSA mapping
            loki_sa = eks_cluster.add_service_account(
                "loki-sa",
                name="loki-iamproxy-service-account",
                namespace="kube-system")

            # Associate the IAM Policy
            loki_policy_statement_json_1 = {
                "Effect":
                "Allow",
                "Action": [
                    "s3:PutObject",
                    "s3:GetObject",
                    "s3:AbortMultipartUpload",
                    "s3:ListBucket",
                    "s3:DeleteObject",
                    "s3:GetObjectVersion",
                    "s3:ListMultipartUploadParts"
                ],
                "Resource": ["*"],
            }
            loki_sa.add_to_principal_policy(
                iam.PolicyStatement.from_json(loki_policy_statement_json_1))

            loki_chart = eks_cluster.add_helm_chart(
                "loki-chart",
                chart="loki",
                version="5.40.1",
                release="loki-for-amp",
                repository="https://grafana.github.io/helm-charts",
                namespace="kube-system",
                create_namespace=False,
                # values={
                #     "loki": {
                #         "commonConfigaaaaaaaa": {
                #             "replication_factor": 1
                #         },
                #         "storage": {
                #             "type": "filesystemaaaa"
                #         },
                #         "singleBinary": {
                #             "replicas": 1
                #         }
                #     }
                # }
                values={
                    "serviceAccount": {
                        "name": "loki-iamproxy-service-account",
                        "annotations": {
                            "eks.amazonaws.com/role-arn": loki_sa.role.role_arn
                        },
                        "create": False,
                    },
                    "loki": {
                        "storage": {
                            "type": "s3"
                        },
                        "s3": {
                            "region": self.region
                        },
                        "bucketNames": {
                            "chunks": "loki-chunk",
                            "ruler": "loki-ruler",
                            "admin": "loki-admin"
                        }
                    }
                }
            )

        # nginx_chart = eks_cluster.add_helm_chart(
        #     "nginx-chart",
        #     chart="ingress-nginx",
        #     version="4.9.0",
        #     release="ingress-nginx",
        #     repository="https://kubernetes.github.io/ingress-nginx",
        #     namespace="ingress-nginx",
        #     create_namespace=True
        # )

        # nginx_chart.node.add_dependency(loki_chart)

        # Run everything via Fargate (i.e. no EC2 Nodes/Managed Node Group)
        # NOTE: You need to add any namespaces other than kube-system and default to this
        # OR create additional Fargate Profiles with the additional namespaces/labels
        if self.node.try_get_context("fargate_only_cluster") == "True":
            # Remove the annotation on CoreDNS forcing it onto EC2 (so it can run on Fargate)
            coredns_fargate_patch = eks.KubernetesPatch(
                self,
                "CoreDNSFargatePatch",
                cluster=eks_cluster,
                resource_name="deployment/coredns",
                resource_namespace="kube-system",
                apply_patch={
                    "spec": {
                        "template": {
                            "metadata": {
                                "annotations": {
                                    "eks.amazonaws.com/compute-type": "fargate"
                                }
                            }
                        }
                    }
                },
                restore_patch={
                    "spec": {
                        "template": {
                            "metadata": {
                                "annotations": {
                                    "eks.amazonaws.com/compute-type": "ec2"
                                }
                            }
                        }
                    }
                },
                patch_type=eks.PatchType.STRATEGIC,
            )

            # Set up a Fargate profile covering both the kube-system and default namespaces
            default_fargate_profile = eks_cluster.add_fargate_profile(
                "DefaultFargateProfile",
                fargate_profile_name="default",
                pod_execution_role=fargate_pod_execution_role,
                selectors=[
                    eks.Selector(namespace="kube-system", ),
                    eks.Selector(namespace="default"),
                ],
            )

        # Send Fargate logs to CloudWatch Logs
        if self.node.try_get_context("fargate_logs_to_cloudwatch") == "True":
            if (self.node.try_get_context("fargate_logs_to_managed_opensearch")
                    == "False"):
                # See https://docs.aws.amazon.com/eks/latest/userguide/fargate-logging.html

                # Add the relevant IAM policy to the Fargate Pod Execution Role
                fargate_cw_logs_policy_statement_json_1 = {
                    "Effect":
                    "Allow",
                    "Action": [
                        "logs:PutLogEvents",
                        "logs:DescribeLogStreams",
                        "logs:DescribeLogGroups",
                        "logs:CreateLogStream",
                        "logs:CreateLogGroup",
                        "logs:PutRetentionPolicy",
                    ],
                    "Resource":
                    "*",
                }
                fargate_pod_execution_role.add_to_principal_policy(
                    iam.PolicyStatement.from_json(
                        fargate_cw_logs_policy_statement_json_1))

                fargate_namespace_manifest = eks_cluster.add_manifest(
                    "FargateLoggingNamespace",
                    {
                        "kind": "Namespace",
                        "apiVersion": "v1",
                        "metadata": {
                            "name": "aws-observability",
                            "labels": {
                                "aws-observability": "enabled"
                            },
                        },
                    },
                )

                fargate_fluentbit_manifest_cw = eks_cluster.add_manifest(
                    "FargateLoggingCW",
                    {
                        "kind": "ConfigMap",
                        "apiVersion": "v1",
                        "metadata": {
                            "name": "aws-logging",
                            "namespace": "aws-observability",
                        },
                        "data": {
                            "output.conf":
                            "[OUTPUT]\n    Name cloudwatch_logs\n    Match   *\n    region "
                            + self.region +
                            "\n    log_group_name fluent-bit-cloudwatch\n    log_stream_prefix from-fluent-bit-\n    auto_create_group true\n    log_retention_days "
                            + str(
                                self.node.try_get_context(
                                    "cloudwatch_container_insights_logs_retention_days"
                                )) + "\n",
                            "parsers.conf":
                            "[PARSER]\n    Name crio\n    Format Regex\n    Regex ^(?<time>[^ ]+) (?<stream>stdout|stderr) (?<logtag>P|F) (?<log>.*)$\n    Time_Key    time\n    Time_Format %Y-%m-%dT%H:%M:%S.%L%z\n",
                            "filters.conf":
                            "[FILTER]\n   Name parser\n   Match *\n   Key_name log\n   Parser crio\n   Reserve_Data On\n   Preserve_Key On\n",
                        },
                    },
                )
                fargate_fluentbit_manifest_cw.node.add_dependency(
                    fargate_namespace_manifest)
            else:
                print(
                    "You need to set only one destination for Fargate Logs to True"
                )

        # # Send Fargate logs to the managed OpenSearch
        # # NOTE This is of limited usefulness without the Kubernetes filter to enrich it with k8s metadata
        # # This is on the roadmap see https://github.com/aws/containers-roadmap/issues/1197
        # # At the moment better to use CloudWatch logs which seperates by source logstream and onward
        # # stream from that to OpenSearch?

        if self.node.try_get_context(
                "fargate_logs_to_managed_opensearch") == "True":
            if self.node.try_get_context(
                    "fargate_logs_to_cloudwatch") == "False":
                # See https://docs.aws.amazon.com/eks/latest/userguide/fargate-logging.html

                # Add the relevant IAM policy to the Fargate Pod Execution Role
                fargate_os_policy_statement_json_1 = {
                    "Effect": "Allow",
                    "Action": ["es:*"],
                    "Resource": [os_domain.domain_arn],
                }
                fargate_pod_execution_role.add_to_principal_policy(
                    iam.PolicyStatement.from_json(
                        fargate_os_policy_statement_json_1))

                fargate_namespace_manifest = eks_cluster.add_manifest(
                    "FargateLoggingNamespace",
                    {
                        "kind": "Namespace",
                        "apiVersion": "v1",
                        "metadata": {
                            "name": "aws-observability",
                            "labels": {
                                "aws-observability": "enabled"
                            },
                        },
                    },
                )

                fargate_fluentbit_manifest_os = eks_cluster.add_manifest(
                    "FargateLoggingOS",
                    {
                        "kind": "ConfigMap",
                        "apiVersion": "v1",
                        "metadata": {
                            "name": "aws-logging",
                            "namespace": "aws-observability",
                        },
                        "data": {
                            "output.conf":
                            "[OUTPUT]\n  Name  es\n  Match *\n  AWS_Region " +
                            self.region + "\n  AWS_Auth On\n  Host " +
                            os_domain.domain_endpoint +
                            "\n  Port 443\n  TLS On\n  Replace_Dots On\n  Logstash_Format On\n"
                        },
                    },
                )
                fargate_fluentbit_manifest_os.node.add_dependency(
                    fargate_namespace_manifest)
            else:
                print(
                    "You need to set only one destination for Fargate Logs to True"
                )

        if self.node.try_get_context("deploy_cert_manager") == "True":

            certmanager_chart = eks_cluster.add_helm_chart(
                "certmanager",
                chart="cert-manager",
                version="1.13.3",
                repository="https://charts.jetstack.io",
                namespace="cert-manager",
                release="cert-manager",
                create_namespace=True,
                values={
                        "installCRDs": True
                }
            )

        if (self.node.try_get_context("deploy_ebs_csi") == "True" and
                self.node.try_get_context("fargate_only_cluster") == "False"):

            ebs_csi_addon_role = iam.Role(
                self,
                'EbsCsiAddonRole',
                # for Role's Trust relationships
                assumed_by=iam.FederatedPrincipal(
                    federated=eks_cluster.open_id_connect_provider.
                    open_id_connect_provider_arn,
                    conditions={'StringEquals': string_like(
                        'kube-system', 'ebs-csi-controller-sa')},
                    assume_role_action='sts:AssumeRoleWithWebIdentity'
                )
            )
            ebs_csi_addon_role.add_managed_policy(iam.ManagedPolicy.from_aws_managed_policy_name(
                "service-role/AmazonEBSCSIDriverPolicy"))

            # Add EBS CSI add-on
            ebs_csi_addon = eks.CfnAddon(
                self,
                "EbsCsiAddon",
                addon_name="aws-ebs-csi-driver",
                cluster_name=eks_cluster.cluster_name,
                resolve_conflicts="OVERWRITE",
                addon_version="v1.26.0-eksbuild.1",
                service_account_role_arn=ebs_csi_addon_role.role_arn
            )

        if self.node.try_get_context("deply_adot") == "True":

            adot_addon_role = iam.Role(
                self,
                'AdotAddonRole',
                # for Role's Trust relationships
                assumed_by=iam.FederatedPrincipal(
                    federated=eks_cluster.open_id_connect_provider.open_id_connect_provider_arn,
                    conditions={'StringEquals': string_like(
                        'opentelemetry-operator-system', 'opentelemetry-operator')},
                    assume_role_action='sts:AssumeRoleWithWebIdentity'
                ),
                role_name="Adot_Addon_Role"
            )
            adot_addon_role.add_managed_policy(iam.ManagedPolicy.from_aws_managed_policy_name(
                "CloudWatchAgentServerPolicy"))
            adot_addon_role.add_managed_policy(iam.ManagedPolicy.from_aws_managed_policy_name(
                "AmazonPrometheusRemoteWriteAccess"))
            adot_addon_role.add_managed_policy(iam.ManagedPolicy.from_aws_managed_policy_name(
                "AWSXRayDaemonWriteAccess"))

            # with open('example.yaml', 'r') as file:
            #     data = yaml.safe_load(file)

            # Add ADOT add-on
            adot_addon = eks.CfnAddon(
                self,
                "AdotAddon",
                addon_name="adot",
                cluster_name=eks_cluster.cluster_name,
                resolve_conflicts="OVERWRITE",
                addon_version="v0.88.0-eksbuild.2",
                service_account_role_arn=adot_addon_role.role_arn
            )

            adot_addon.node.add_dependency(certmanager_chart)

        eks_cluster.add_service_account(
            "ServiceAccountForIRSAPods",
            name="eshop-eks-sa",
            namespace="default",
            annotations={'eks.amazonaws.com/role-arn': 'arn:aws:iam::' +
                         self.node.try_get_context(
                             "account") + ':role/EshopEksForOidcSa'})

        # Deploy the manifests from service-accounts.yaml
        service_accounts_yaml_file = open(
            "service-accounts.yaml", "r")
        service_accounts_yaml = list(
            yaml.load_all(service_accounts_yaml_file,
                          Loader=yaml.FullLoader))
        service_accounts_yaml_file.close()
        loop_iteration = 0
        for value in service_accounts_yaml:
            # print(value)
            loop_iteration = loop_iteration + 1
            manifest_id = "ServiceAccountsManifest" + \
                str(loop_iteration)
            manifest = eks_cluster.add_manifest(manifest_id, value)

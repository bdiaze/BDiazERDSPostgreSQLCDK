using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SecretsManager;
using Amazon.CDK.AWS.SSM;
using Constructs;
using System.Collections.Generic;
using InstanceType = Amazon.CDK.AWS.EC2.InstanceType;

namespace BDiazErdsPostgreSql
{
    public class BDiazERDSPostgreSqlStack : Stack
    {
        internal BDiazERDSPostgreSqlStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props) {
            string appName = System.Environment.GetEnvironmentVariable("APP_NAME")!;
            string vpcId = System.Environment.GetEnvironmentVariable("VPC_ID")!;
            string subnetId1 = System.Environment.GetEnvironmentVariable("SUBNET_ID_1")!;
            string subnetId2 = System.Environment.GetEnvironmentVariable("SUBNET_ID_2")!;
            string masterUsername = System.Environment.GetEnvironmentVariable("MASTER_USERNAME")!;
            string masterPassword = System.Environment.GetEnvironmentVariable("MASTER_PASSWORD")!;
            string lambdaSecurityGroupId = System.Environment.GetEnvironmentVariable("LAMBDA_SECURITY_GROUP_ID")!;
            string ec2SecurityGroupId = System.Environment.GetEnvironmentVariable("EC2_SECURITY_GROUP_ID")!;

            // Se obtienen passwords de las otras aplicaciones para almacenar en el mismo secret...
            string subapp01Name = System.Environment.GetEnvironmentVariable("SUBAPP_01_NAME")!;
            string subapp01Database = System.Environment.GetEnvironmentVariable("SUBAPP_01_DATABASE")!;
            string subapp01Username = System.Environment.GetEnvironmentVariable("SUBAPP_01_USERNAME")!;
            string subapp01Password = System.Environment.GetEnvironmentVariable("SUBAPP_01_PASSWORD")!;

            string subapp02Name = System.Environment.GetEnvironmentVariable("SUBAPP_02_NAME")!;
            string subapp02Database = System.Environment.GetEnvironmentVariable("SUBAPP_02_DATABASE")!;
            string subapp02Username = System.Environment.GetEnvironmentVariable("SUBAPP_02_USERNAME")!;
            string subapp02Password = System.Environment.GetEnvironmentVariable("SUBAPP_02_PASSWORD")!;

            IVpc vpc = Vpc.FromLookup(this, $"{appName}Vpc", new VpcLookupOptions {
                VpcId = vpcId
            });

            ISubnet subnet1 = Subnet.FromSubnetId(this, $"{appName}Subnet1", subnetId1);
            ISubnet subnet2 = Subnet.FromSubnetId(this, $"{appName}Subnet2", subnetId2);

            ISubnetGroup subnetGroup = new SubnetGroup(this, $"{appName}SubnetGroup", new SubnetGroupProps {
                SubnetGroupName = $"{appName}PrivateSubnetGroup",
                Description = $"Private Subnet Group - {appName}",
                Vpc = vpc,
                VpcSubnets = new SubnetSelection {
                    Subnets = [subnet1, subnet2]
                }
            });

            ISecurityGroup securityGroup = new SecurityGroup(this, $"{appName}RDSSecurityGroup", new SecurityGroupProps {
                Vpc = vpc,
                SecurityGroupName = $"{appName}RDSSecurityGroup",
                Description = $"RDS Security Group - {appName}",
                AllowAllOutbound = true
            });

            securityGroup.AddIngressRule(Peer.SecurityGroupId(lambdaSecurityGroupId), Port.POSTGRES, "Ingress para funciones Lambda");
            securityGroup.AddIngressRule(Peer.SecurityGroupId(ec2SecurityGroupId), Port.POSTGRES, "Ingress para instancias EC2");


            ParameterGroup parameterGroup = new(this, $"{appName}ParameterGroup", new ParameterGroupProps {
                Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps {
                    Version = PostgresEngineVersion.VER_17_2
                }),
                Name = $"{appName}ParameterGroup",
            });

            IRole role = new Role(this, $"{appName}RDSMonitoringRole", new RoleProps {
                AssumedBy = new ServicePrincipal("monitoring.rds.amazonaws.com"),
                RoleName = $"{appName}RDSMonitoringRole",
                ManagedPolicies = [
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonRDSEnhancedMonitoringRole")
                ]
            });

            DatabaseInstance instance = new(this, $"{appName}DatabaseInstance", new DatabaseInstanceProps {
                InstanceIdentifier = $"{appName}PostgreSQLInstance",
                Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps {
                    Version = PostgresEngineVersion.VER_17_2
                }),
                ParameterGroup = parameterGroup,
                Credentials = Credentials.FromPassword(masterUsername, SecretValue.UnsafePlainText(masterPassword)),
                InstanceType = InstanceType.Of(InstanceClass.BURSTABLE4_GRAVITON, InstanceSize.MICRO),
                StorageType = StorageType.GP2,
                AllocatedStorage = 20,
                MaxAllocatedStorage = 100,
                Vpc = vpc,
                SubnetGroup = subnetGroup,
                PubliclyAccessible = false,
                SecurityGroups = [securityGroup],
                EnablePerformanceInsights = true,
                PerformanceInsightRetention = PerformanceInsightRetention.DEFAULT,
                MonitoringInterval = Duration.Minutes(1),
                MonitoringRole = role,
                CloudwatchLogsExports = ["postgresql"],
                CloudwatchLogsRetention = RetentionDays.ONE_MONTH,
                RemovalPolicy = RemovalPolicy.RETAIN,
            });

            // Se crea parámetro con información de conexión a la base de datos...
            _ = new Secret(this, $"{appName}RDSPostgreSQLSecret", new SecretProps { 
                SecretName = $"/{appName}/RDSPostgreSQL/ConnectionString",
                Description = $"Connection String de la base de datos RDS PostgreSQL de la aplicación {appName}",
                SecretObjectValue = new Dictionary<string, SecretValue> {
                    { "Host", SecretValue.UnsafePlainText(instance.DbInstanceEndpointAddress) },
                    { "Port", SecretValue.UnsafePlainText(instance.DbInstanceEndpointPort) },
                    { "MasterUser", SecretValue.UnsafePlainText(masterUsername) },
                    { "MasterPassword", SecretValue.UnsafePlainText(masterPassword) },
                    // Se añaden las passwords de las subapps...
                    { $"{subapp01Name}Database", SecretValue.UnsafePlainText(subapp01Database) },
                    { $"{subapp01Name}Username", SecretValue.UnsafePlainText(subapp01Username) },
                    { $"{subapp01Name}Password", SecretValue.UnsafePlainText(subapp01Password) },
                    { $"{subapp02Name}Database", SecretValue.UnsafePlainText(subapp02Database) },
                    { $"{subapp02Name}Username", SecretValue.UnsafePlainText(subapp02Username) },
                    { $"{subapp02Name}Password", SecretValue.UnsafePlainText(subapp02Password) },
                },
             });
        }
    }
}

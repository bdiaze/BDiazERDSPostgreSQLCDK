using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SecretsManager;
using Amazon.CDK.CustomResources;
using Constructs;
using System;
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
            string defaultDatabase = System.Environment.GetEnvironmentVariable("DEFAULT_DATABASE")!;
            string masterUsername = System.Environment.GetEnvironmentVariable("MASTER_USERNAME")!;
            string masterPassword = System.Environment.GetEnvironmentVariable("MASTER_PASSWORD")!;
            string lambdaSecurityGroupId = System.Environment.GetEnvironmentVariable("LAMBDA_SECURITY_GROUP_ID")!;
            string ec2SecurityGroupId = System.Environment.GetEnvironmentVariable("EC2_SECURITY_GROUP_ID")!;

            // Se obtienen passwords de las otras aplicaciones para almacenar en el mismo secret...
            string subapp01Name = System.Environment.GetEnvironmentVariable("SUBAPP_01_NAME")!;
            string subapp01Database = System.Environment.GetEnvironmentVariable("SUBAPP_01_DATABASE")!;
            string subapp01AdmUsername = System.Environment.GetEnvironmentVariable("SUBAPP_01_ADM_USERNAME")!;
            string subapp01AdmPassword = System.Environment.GetEnvironmentVariable("SUBAPP_01_ADM_PASSWORD")!;
            string subapp01AppUsername = System.Environment.GetEnvironmentVariable("SUBAPP_01_APP_USERNAME")!;
            string subapp01AppPassword = System.Environment.GetEnvironmentVariable("SUBAPP_01_APP_PASSWORD")!;

            string subapp02Name = System.Environment.GetEnvironmentVariable("SUBAPP_02_NAME")!;
            string subapp02Database = System.Environment.GetEnvironmentVariable("SUBAPP_02_DATABASE")!;
            string subapp02AdmUsername = System.Environment.GetEnvironmentVariable("SUBAPP_02_ADM_USERNAME")!;
            string subapp02AdmPassword = System.Environment.GetEnvironmentVariable("SUBAPP_02_ADM_PASSWORD")!;
            string subapp02AppUsername = System.Environment.GetEnvironmentVariable("SUBAPP_02_APP_USERNAME")!;
            string subapp02AppPassword = System.Environment.GetEnvironmentVariable("SUBAPP_02_APP_PASSWORD")!;

            // Se obtienen variables de entorno para la creación de la lambda de ejecución inicial...
            string privateWithInternetId1 = System.Environment.GetEnvironmentVariable("PRIVATE_WITH_INTERNET_ID_1")!;
            string privateWithInternetId2 = System.Environment.GetEnvironmentVariable("PRIVATE_WITH_INTERNET_ID_2")!;
            string initialCreationHandler = System.Environment.GetEnvironmentVariable("INITIAL_CREATION_HANDLER")!;
            string initialCreationPublishZip = System.Environment.GetEnvironmentVariable("INITIAL_CREATION_PUBLISH_ZIP")!;

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

            SecurityGroup securityGroup = new(this, $"{appName}RDSSecurityGroup", new SecurityGroupProps {
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
            Secret secret = new(this, $"{appName}RDSPostgreSQLSecret", new SecretProps { 
                SecretName = $"/{appName}/RDSPostgreSQL/ConnectionString",
                Description = $"Connection String de la base de datos RDS PostgreSQL de la aplicacion {appName}",
                SecretObjectValue = new Dictionary<string, SecretValue> {
                    { "Host", SecretValue.UnsafePlainText(instance.DbInstanceEndpointAddress) },
                    { "Port", SecretValue.UnsafePlainText(instance.DbInstanceEndpointPort) },
                    { "DefaultDatabase", SecretValue.UnsafePlainText(defaultDatabase) },
                    { "MasterUser", SecretValue.UnsafePlainText(masterUsername) },
                    { "MasterPassword", SecretValue.UnsafePlainText(masterPassword) },
                    // Se añaden las passwords de las subapps...
                    { $"{subapp01Name}Database", SecretValue.UnsafePlainText(subapp01Database) },
                    { $"{subapp01Name}AdmUsername", SecretValue.UnsafePlainText(subapp01AdmUsername) },
                    { $"{subapp01Name}AdmPassword", SecretValue.UnsafePlainText(subapp01AdmPassword) },
                    { $"{subapp01Name}AppUsername", SecretValue.UnsafePlainText(subapp01AppUsername) },
                    { $"{subapp01Name}AppPassword", SecretValue.UnsafePlainText(subapp01AppPassword) },
                    { $"{subapp02Name}Database", SecretValue.UnsafePlainText(subapp02Database) },
                    { $"{subapp02Name}AdmUsername", SecretValue.UnsafePlainText(subapp02AdmUsername) },
                    { $"{subapp02Name}AdmPassword", SecretValue.UnsafePlainText(subapp02AdmPassword) },
                    { $"{subapp02Name}AppUsername", SecretValue.UnsafePlainText(subapp02AppUsername) },
                    { $"{subapp02Name}AppPassword", SecretValue.UnsafePlainText(subapp02AppPassword) },
                },
            });

            // Se crea función lambda que ejecute scripts para la creación de las bases de datos, usuarios y permisos...
            // Primero creación de log group lambda...
            LogGroup logGroupLambda = new(this, $"{appName}RDSPostgreSQLInitialCreationLambdaLogGroup", new LogGroupProps {
                LogGroupName = $"/aws/lambda/{appName}RDSPostgreSQLInitialCreationLambda/logs",
                Retention = RetentionDays.ONE_MONTH,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // Luego la creación del rol para la función lambda...
            IRole roleLambda = new Role(this, $"{appName}RDSPostgreSQLInitialCreationLambdaRole", new RoleProps {
                RoleName = $"{appName}RDSPostgreSQLInitialCreationLambdaRole",
                Description = $"Role para Lambda de creacion inicial {appName} RDS PostgreSQL",
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                ManagedPolicies = [
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaVPCAccessExecutionRole"),
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole"),
                ],
                InlinePolicies = new Dictionary<string, PolicyDocument> {
                    {
                        $"{appName}RDSPostgreSQLInitialCreationLambdaPolicy",
                        new PolicyDocument(new PolicyDocumentProps {
                            Statements = [
                                new PolicyStatement(new PolicyStatementProps{
                                    Sid = $"{appName}AccessToSecretManager",
                                    Actions = [
                                        "secretsmanager:GetSecretValue"
                                    ],
                                    Resources = [
                                        secret.SecretFullArn,
                                    ],
                                })
                            ]
                        })
                    }
                }
            });

            // Y el security group...
            SecurityGroup securityGroupLambda = new(this, $"{appName}InitialCreationLambdaSecurityGroup", new SecurityGroupProps {
                Vpc = vpc,
                SecurityGroupName = $"{appName}RDSPostgreSQLInitialCreationLambdaSecurityGroup",
                Description = $"Security Group para Lambda de creacion inicial {appName} RDS PostgreSQL",
                AllowAllOutbound = true
            });
            securityGroup.AddIngressRule(Peer.SecurityGroupId(securityGroupLambda.SecurityGroupId), Port.POSTGRES, $"Ingress para funcion lambda de creacion inicial {appName} RDS PostgreSQL");

            // Creación de la función lambda...
            ISubnet privateWithInternet1 = Subnet.FromSubnetId(this, $"{appName}PrivateSubnetWithInternet1", privateWithInternetId1);
            ISubnet privateWithInternet2 = Subnet.FromSubnetId(this, $"{appName}PrivateSubnetWithInternet2", privateWithInternetId2);
            Function function = new(this, $"{appName}InitialCreationLambda", new FunctionProps {
                Runtime = Runtime.DOTNET_8,
                Handler = initialCreationHandler,
                Code = Code.FromAsset(initialCreationPublishZip),
                FunctionName = $"{appName}RDSPostgreSQLInitialCreationLambda",
                Timeout = Duration.Seconds(2 * 60),
                MemorySize = 256,
                Architecture = Architecture.ARM_64,
                LogGroup = logGroupLambda,
                Environment = new Dictionary<string, string> {
                    { "SECRET_ARN_CONNECTION_STRING", secret.SecretFullArn },
                    { "SUBAPP_01_NAME", subapp01Name },
                    { "SUBAPP_02_NAME", subapp02Name },
                },
                Vpc = vpc,
                VpcSubnets = new SubnetSelection {
                    Subnets = [privateWithInternet1, privateWithInternet2]
                },
                SecurityGroups = [securityGroupLambda],
                Role = roleLambda,
            });

            // Se gatilla la lambda...
            _ = new AwsCustomResource(this, $"{appName}InitialCreationTrigger", new AwsCustomResourceProps {
                Policy = AwsCustomResourcePolicy.FromStatements([
                    new PolicyStatement(new PolicyStatementProps{
                        Actions = [ "lambda:InvokeFunction" ],
                        Resources = [ function.FunctionArn ]
                    })
                ]),
                Timeout = Duration.Seconds(2 * 60),
                OnUpdate = new AwsSdkCall {
                    Service = "Lambda",
                    Action = "invoke",
                    Parameters = new {
                        function.FunctionName,
                        InvocationType = "Event"
                    },
                    PhysicalResourceId = PhysicalResourceId.Of(DateTime.Now.ToString())
                }
            });
        }
    }
}

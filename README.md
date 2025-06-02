# RDS PostgreSQL con CDK .NET

- [RDS PostgreSQL con CDK .NET](#rds-postgresql-con-cdk-net)
  - [Introducción](#introducción)
  - [Recursos Requeridos](#recursos-requeridos)
    - [VPC y Subnets](#vpc-y-subnets)
    - [Grupos de Seguridad que Requieren Acceso](#grupos-de-seguridad-que-requieren-acceso)
  - [Recursos Creados](#recursos-creados)
    - [Grupo de Subnets](#grupo-de-subnets)
    - [Grupo de Seguridad](#grupo-de-seguridad)
    - [Grupo de Parámetros](#grupo-de-parámetros)
    - [Rol de Monitoreo](#rol-de-monitoreo)
    - [Instancia de Base de Datos](#instancia-de-base-de-datos)
    - [Secreto](#secreto)
    - [Lambda de Configuración Inicial](#lambda-de-configuración-inicial)
      - [Grupo de Logs de la Lambda](#grupo-de-logs-de-la-lambda)
      - [Rol de la Lambda](#rol-de-la-lambda)
      - [Grupo de Seguridad de la Lambda](#grupo-de-seguridad-de-la-lambda)
      - [Lambda](#lambda)
      - [Recurso Customizado para Gatillar la Lambda](#recurso-customizado-para-gatillar-la-lambda)
  - [Despliegue](#despliegue)
    - [Variables y Secretos de Entorno](#variables-y-secretos-de-entorno)

## Introducción

* El siguiente repositorio es para crear una base de datos usando el servicio [AWS RDS](https://aws.amazon.com/es/rds/).
* Además, incluye la configuración inicial de dicha base de datos, como lo serían la creación de usuarios administradores y databases, usando el servicio [AWS Lambda](https://aws.amazon.com/es/lambda/).
* La infraestructura se despliega mediante IaC, usando [AWS CDK en .NET 8.0](https://docs.aws.amazon.com/cdk/api/v2/dotnet/api/).
* El despliegue CI/CD se lleva a cabo mediante  [GitHub Actions](https://github.com/features/actions).

## Recursos Requeridos

### VPC y Subnets

Es necesario contar con la información de la VPC y subnets privadas a las cuales pertenecerá la instancia de base de datos.

<ins>Código para obtener VPC existente:</ins>

```csharp
using Amazon.CDK.AWS.EC2;

IVpc vpc = Vpc.FromLookup(this, ..., new VpcLookupOptions {
    VpcId = ...
});
```

<ins>Código para obtener Subnets existentes:</ins>

```csharp
using Amazon.CDK.AWS.EC2;

ISubnet subnet1 = Subnet.FromSubnetId(this, ..., ...);
ISubnet subnet2 = Subnet.FromSubnetId(this, ..., ...);
```

### Grupos de Seguridad que Requieren Acceso

Opcionalmente, es posible contar con múltiples grupos de seguridad a los cuales se le habilitará una regla de ingreso al grupo de seguridad de la base de datos, permitiendo que los recursos asociados a dichos grupos puedan iniciar una conexión hacia la base de datos. 

<ins>Código para crear Reglas de Ingreso al grupo de seguridad de la base de datos:</ins>
```csharp
using Amazon.CDK.AWS.EC2;

securityGroup.AddIngressRule(Peer.SecurityGroupId(...), Port.POSTGRES, ...);
securityGroup.AddIngressRule(Peer.SecurityGroupId(...), Port.POSTGRES, ...);

```

## Recursos Creados

### Grupo de Subnets

Se crea un grupo de subnets asociado a la VPC y subnets privadas, el cual será usado en la configuración de red de la instancia de base de datos.

<ins>Código para crear Grupo de Subnets:</ins>
```csharp
using Amazon.CDK.AWS.RDS;

ISubnetGroup subnetGroup = new SubnetGroup(this, ..., new SubnetGroupProps {
    SubnetGroupName = ...,
    Description = ...,
    Vpc = vpc,
    VpcSubnets = new SubnetSelection {
        Subnets = [subnet1, subnet2]
    }
});
```

### Grupo de Seguridad

Grupo de seguridad usado para la base de datos. Aquí es donde se deberán crear las reglas de ingreso para los grupos existentes que requieran acceso a la base de datos.

<ins>Código para crear Grupo de Seguridad:</ins>
```csharp
using Amazon.CDK.AWS.EC2;

SecurityGroup securityGroup = new(this, ..., new SecurityGroupProps {
    Vpc = vpc,
    SecurityGroupName = ...,
    Description = ...,
    AllowAllOutbound = true
});

securityGroup.AddIngressRule(Peer.SecurityGroupId(...), Port.POSTGRES, ...);
securityGroup.AddIngressRule(Peer.SecurityGroupId(...), Port.POSTGRES, ...);
```

### Grupo de Parámetros

Grupo de parámetros RDS. En este caso solo se configura la versión 17 del motor de PostgreSQL.

<ins>Código para crear Grupo de Parámetros:</ins>
```csharp
using Amazon.CDK.AWS.RDS;

ParameterGroup parameterGroup = new(this, ..., new ParameterGroupProps {
    Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps {
        Version = PostgresEngineVersion.VER_17_2
    }),
    Name = ...,
});
```

### Rol de Monitoreo

Rol de IAM que se usará para monitorear la instancia.

<ins>Código para crear Rol de Monitoreo:</ins>
```csharp
using Amazon.CDK.AWS.IAM;

IRole role = new Role(this, ..., new RoleProps {
    AssumedBy = new ServicePrincipal("monitoring.rds.amazonaws.com"),
    RoleName = ...,
    ManagedPolicies = [
        ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonRDSEnhancedMonitoringRole")
    ]
});
```

### Instancia de Base de Datos

<ins>Código para crear Instancia de Base de Datos:</ins>
```csharp
using Amazon.CDK.AWS.RDS;

DatabaseInstance instance = new(this, ..., new DatabaseInstanceProps {
    InstanceIdentifier = ...,
    Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps {
        Version = PostgresEngineVersion.VER_17_2
    }),
    ParameterGroup = parameterGroup,
    Credentials = Credentials.FromPassword(..., SecretValue.UnsafePlainText(...)),
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
```

### Secreto

Secreto asociado a las credenciales, host y ports de la instancia recien creada. Los recursos que deseen conectarse a la base de datos deberán extraer los datos de conexión desde este secreto.

<ins>Código para crear Secreto:</ins>
```csharp
using Amazon.CDK.AWS.SecretsManager;

Secret secret = new(this, ..., new SecretProps { 
    SecretName = ...,
    Description = ...,
    SecretObjectValue = new Dictionary<string, SecretValue> {
        { "Host", SecretValue.UnsafePlainText(instance.DbInstanceEndpointAddress) },
        { "Port", SecretValue.UnsafePlainText(instance.DbInstanceEndpointPort) },
        { "DefaultDatabase", SecretValue.UnsafePlainText(...) },
        { "MasterUser", SecretValue.UnsafePlainText(...) },
        { "MasterPassword", SecretValue.UnsafePlainText(...) },
        ...
    },
});
```

> [!NOTE]
> Junto con la información general de la base de datos (host, port, default database, master user y password), es posible almacenar información adicional de cada subaplicación relacionada con esta base de datos, por ejemplo, para la subaplicación "01" se puede almacenar el nombre de la database, las credenciales de un usuario administrador y las credenciales de un usuario de aplicación, lo mismo para la subaplicación "02".

```csharp
SecretObjectValue = new Dictionary<string, SecretValue> {
    ...
    { $"{subapp01Name}Database", SecretValue.UnsafePlainText(...) },
    { $"{subapp01Name}AdmUsername", SecretValue.UnsafePlainText(...) },
    { $"{subapp01Name}AdmPassword", SecretValue.UnsafePlainText(...) },
    { $"{subapp01Name}AppUsername", SecretValue.UnsafePlainText(...) },
    { $"{subapp01Name}AppPassword", SecretValue.UnsafePlainText(...) },
    { $"{subapp02Name}Database", SecretValue.UnsafePlainText(...) },
    { $"{subapp02Name}AdmUsername", SecretValue.UnsafePlainText(...) },
    { $"{subapp02Name}AdmPassword", SecretValue.UnsafePlainText(...) },
    { $"{subapp02Name}AppUsername", SecretValue.UnsafePlainText(...) },
    { $"{subapp02Name}AppPassword", SecretValue.UnsafePlainText(...) },
},
```
> [!CAUTION]
> **¡Ojo! almacenar distintos valores no relacionados en un mismo secreto no es recomendable**, dado que cada subaplicación con acceso al secreto tendría acceso a todos los valores contenidos en dicho secreto, independiente de que solo haya requerido uno de estos valores.
>
> Lo correcto en este caso sería tener cinco secretos diferentes, uno para la credencial maestra, otros dos para las credenciales de administración, además de otros dos para las credenciales de aplicación de los subaplicativos "01" y "02", pero por temas de costos se decidió agrupar todos estos valores en un único secreto. 

### Lambda de Configuración Inicial

Además de crear la instancia de base de datos, se creará una lambda cuyo proposito es conectarse a la instancia de base de datos para ejecutar la configuración inicial de ésta. Para ello se crearán los siguientes recursos:

#### Grupo de Logs de la Lambda

<ins>Código para crear Grupo de Logs:</ins>
```csharp
using Amazon.CDK.AWS.Logs;

LogGroup logGroupLambda = new(this, ..., new LogGroupProps {
    LogGroupName = ...,
    Retention = RetentionDays.ONE_MONTH,
    RemovalPolicy = RemovalPolicy.DESTROY
});
```

#### Rol de la Lambda

El rol a crear para la Lambda. Junto con tener los permisos básicos requeridos por la Lambda (AWSLambdaVPCAccessExecutionRole y AWSLambdaBasicExecutionRole), también tendrá permiso de lectura sobre el secreto creado con los datos de conexión de la base de datos.

<ins>Código para crear Rol:</ins>
```csharp
using Amazon.CDK.AWS.IAM;

IRole roleLambda = new Role(this, ..., new RoleProps {
    RoleName = ...,
    Description = ...,
    AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
    ManagedPolicies = [
        ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaVPCAccessExecutionRole"),
        ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole"),
    ],
    InlinePolicies = new Dictionary<string, PolicyDocument> {
        {
            ...,
            new PolicyDocument(new PolicyDocumentProps {
                Statements = [
                    new PolicyStatement(new PolicyStatementProps{
                        Sid = ...,
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
```

#### Grupo de Seguridad de la Lambda

Grupo de seguridad de la Lambda. Es necesario habilitar una regla de ingreso que permita la conexión entre éste y el grupo de seguridad de la base de datos.

<ins>Código para crear Grupo de Seguridad:</ins>
```csharp
using Amazon.CDK.AWS.EC2;

SecurityGroup securityGroupLambda = new(this, ..., new SecurityGroupProps {
    Vpc = vpc,
    SecurityGroupName = ...,
    Description = ...,
    AllowAllOutbound = true
});
securityGroup.AddIngressRule(Peer.SecurityGroupId(securityGroupLambda.SecurityGroupId), Port.POSTGRES, ...);
```

#### Lambda

<ins>Código para crear Lambda:</ins>
```csharp
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Lambda;

ISubnet privateWithInternet1 = Subnet.FromSubnetId(this, ..., ...);
ISubnet privateWithInternet2 = Subnet.FromSubnetId(this, ..., ...);
Function function = new(this, ..., new FunctionProps {
    Runtime = Runtime.DOTNET_8,
    Handler = ...,
    Code = Code.FromAsset(...),
    FunctionName = ...,
    Timeout = Duration.Seconds(2 * 60),
    MemorySize = 256,
    Architecture = Architecture.ARM_64,
    LogGroup = logGroupLambda,
    Environment = new Dictionary<string, string> {
        { "SECRET_ARN_CONNECTION_STRING", secret.SecretFullArn },
        { "SUBAPP_01_NAME", ... },
        { "SUBAPP_02_NAME", ... },
    },
    Vpc = vpc,
    VpcSubnets = new SubnetSelection {
        Subnets = [privateWithInternet1, privateWithInternet2]
    },
    SecurityGroups = [securityGroupLambda],
    Role = roleLambda,
});
```

> [!NOTE]
> A diferencia de la base de datos, se optó por usar las subnets privadas con internet para la Lambda.

#### Recurso Customizado para Gatillar la Lambda

<ins>Código para crear Recurso Customizado:</ins>
```csharp
using Amazon.CDK.CustomResources;

_ = new AwsCustomResource(this, ..., new AwsCustomResourceProps {
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
        Parameters = new Dictionary<string, object> {
            { "FunctionName", function.FunctionName },
            { "InvocationType", "Event" },
            { "Payload", "\"\"" }
        },
        PhysicalResourceId = PhysicalResourceId.Of(DateTime.Now.ToString())
    }
});
```

## Despliegue

El despliegue se lleva a cabo mediante GitHub Actions, para ello se configura la receta de despliegue con los siguientes pasos:

| Paso | Comando | Descripción |
|------|---------|-------------|
| Checkout Repositorio | `actions/checkout@v4` | Se descarga el repositorio en runner. |
| Instalar .NET | `actions/setup-dotnet@v4` | Se instala .NET en el runner. |
| Instalar Node.js | `actions/setup-node@v4` | Se instala Node.js en el runner. | 
| Instalar AWS CDK | `npm install -g aws-cdk` | Se instala aws-cdk con NPM. |
| Publish .NET Lambda |  `dotnet publish --property:PublishDir=../publish --runtime linux-arm64` | Se publica la función Lambda. |
| Compress Publish Directory | `zip -r -q -T ./publish.zip ./*` | Se comprime el directorio de publicación de la función Lambda. |
| Configure AWS Credentials | `aws-actions/configure-aws-credentials` | Se configuran credenciales para despliegue en AWS. |
| CDK Synth | `cdk synth` | Se sintetiza la aplicación CDK. |
| CDK Diff | `cdk --app cdk.out diff` | Se obtienen las diferencias entre nueva versión y versión desplegada. |
| CDK Deploy | `cdk --app cdk.out deploy --require-approval never` | Se despliega la aplicación CDK. |

### Variables y Secretos de Entorno

A continuación se presentan las variables que se deben configurar en el Environment para el correcto despliegue:

| Variable de Entorno | Tipo | Descripción |
|---------------------|------|-------------|
| `VERSION_DOTNET` | Variable | Versión del .NET del CDK. Por ejemplo "8". |
| `VERSION_NODEJS` | Variable | Versión de Node.js. Por ejemplo "20". |
| `ARN_GITHUB_ROLE` | Variable | ARN del Rol en IAM que se usará para el despliegue. |
| `ACCOUNT_AWS` | Variable | ID de la cuenta AWS donde desplegar. |
| `REGION_AWS` | Variable | Región primaria donde desplegar. Por ejemplo "us-west-1". |
| `DIRECTORIO_CDK` | Variable | Directorio donde se encuentra archivo cdk.json. En este caso sería ".". |
| `APP_NAME` | Variable | El nombre de la aplicación a desplegar. |
| `DEFAULT_DATABASE` | Variable | Nombre de la database por defecto. En este caso sería "postgres". |
| `EC2_SECURITY_GROUP_ID` | Variable | ID del grupo de seguridad existente para EC2 que requiere acceso a la base de datos. |
| `LAMBDA_SECURITY_GROUP_ID` | Variable | ID del grupo de seguridad existente para Lambda que requiere acceso a la base de datos. |
| `VPC_ID` | Variable | ID de la VPC donde se desplegará la base de datos. |
| `SUBNET_ID_1` | Variable | ID de la subnet privada donde se desplegará la base de datos. |
| `SUBNET_ID_2` | Variable | ID de la subnet privada donde se desplegará la base de datos. |
| `SUBAPP_01_NAME` | Variable | Nombre de la subaplicación "01".  |
| `SUBAPP_02_NAME` | Variable | Nombre de la subaplicación "02". |
| `PRIVATE_WITH_INTERNET_ID_1` | Variable | ID de la subnet privada con acceso a internet para la Lambda. |
| `PRIVATE_WITH_INTERNET_ID_2` | Variable | ID de la subnet privada con acceso a internet para la Lambda. |
| `INITIAL_CREATION_DIRECTORY` | Variable | Directorio donde se encuentra la solución .NET de la Lambda. En este caso sería "./InitialCreationLambda".|
| `INITIAL_CREATION_HANDLER` | Variable | Handler asociado a la función Lambda. En este caso sería "InitialCreationLambda::InitialCreationLambda.Function::FunctionHandler".|
| `INITIAL_CREATION_PUBLISH_ZIP` | Variable | Ubicación de archivo comprimido Zip de la publicación de la Lambda. En este caso sería "./InitialCreationLambda/publish/publish.zip". |
| `MASTER_USERNAME` | Secreto | Usuario maestro de la base de datos. |
| `MASTER_PASSWORD` | Secreto | Contraseña maestra de la base de datos. |
| `SUBAPP_01_DATABASE` | Secreto | Nombre de la database de la subaplicación "01". |
| `SUBAPP_01_ADM_USERNAME` | Secreto | Usuario de administración de la subaplicación "01". |
| `SUBAPP_01_ADM_PASSWORD` | Secreto | Contraseña del usuario de administración de la subaplicación "01". |
| `SUBAPP_01_APP_USERNAME` | Secreto | Usuario de aplicación de la subaplicación "01". |
| `SUBAPP_01_APP_PASSWORD` | Secreto | Contraseña del usuario de aplicación de la subaplicación "01". |
| `SUBAPP_02_DATABASE` | Secreto | Nombre de la database de la subaplicación "02". |
| `SUBAPP_02_ADM_USERNAME` | Secreto | Usuario de administración de la subaplicación "02". |
| `SUBAPP_02_ADM_PASSWORD` | Secreto | Contraseña del usuario de administración de la subaplicación "02". |
| `SUBAPP_02_APP_USERNAME` | Secreto | Usuario de aplicación de la subaplicación "02". |
| `SUBAPP_02_APP_PASSWORD` | Secreto | Contraseña del usuario de aplicación de la subaplicación "02". |

import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as rds from 'aws-cdk-lib/aws-rds';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as ecs from 'aws-cdk-lib/aws-ecs';
import * as ecr from 'aws-cdk-lib/aws-ecr';
import * as elbv2 from 'aws-cdk-lib/aws-elasticloadbalancingv2';
import * as logs from 'aws-cdk-lib/aws-logs';
import * as secretsmanager from 'aws-cdk-lib/aws-secretsmanager';
import * as servicediscovery from 'aws-cdk-lib/aws-servicediscovery';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as origins from 'aws-cdk-lib/aws-cloudfront-origins';
import { Construct } from 'constructs';

export interface SekibanDynamoDbStackProps extends cdk.StackProps {
  config: any;
  envName: string;
}

export class SekibanDynamoDbStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: SekibanDynamoDbStackProps) {
    super(scope, id, props);

    const { config, envName } = props;

    // Check if HTTPS is enabled for external ALB (certificate ARN provided)
    const httpsEnabled = config.alb?.certificateArn && config.alb.certificateArn !== '';

    // ========================================
    // VPC
    // ========================================
    const vpc = new ec2.Vpc(this, 'Vpc', {
      ipAddresses: ec2.IpAddresses.cidr(config.vpc.cidr),
      maxAzs: config.vpc.maxAzs,
      natGateways: 1,
      subnetConfiguration: [
        {
          name: 'Public',
          subnetType: ec2.SubnetType.PUBLIC,
          cidrMask: 24,
        },
        {
          name: 'Private',
          subnetType: ec2.SubnetType.PRIVATE_WITH_EGRESS,
          cidrMask: 24,
        },
      ],
    });

    // ========================================
    // Security Groups
    // ========================================
    const albSg = new ec2.SecurityGroup(this, 'AlbSg', {
      vpc,
      description: 'ALB Security Group',
      allowAllOutbound: true,
    });
    albSg.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(80), 'Allow HTTP');
    albSg.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(443), 'Allow HTTPS');

    const ecsSg = new ec2.SecurityGroup(this, 'EcsSg', {
      vpc,
      description: 'ECS Task Security Group',
      allowAllOutbound: true,
    });
    // External ALB -> ECS HTTP (WebNext)
    ecsSg.addIngressRule(albSg, ec2.Port.tcp(config.webnext.httpPort), 'Allow ALB to WebNext HTTP');
    // ECS (WebNext) -> ECS (API) via Cloud Map
    ecsSg.addIngressRule(ecsSg, ec2.Port.tcp(config.orleans.httpPort), 'Allow WebNext to API HTTP');
    // ECS <-> ECS Orleans Silo
    ecsSg.addIngressRule(ecsSg, ec2.Port.tcp(config.orleans.siloPort), 'Allow Silo-to-Silo');
    // ECS <-> ECS Orleans Gateway
    ecsSg.addIngressRule(ecsSg, ec2.Port.tcp(config.orleans.gatewayPort), 'Allow Gateway');

    const rdsSg = new ec2.SecurityGroup(this, 'RdsSg', {
      vpc,
      description: 'RDS Security Group',
      allowAllOutbound: false,
    });
    rdsSg.addIngressRule(ecsSg, ec2.Port.tcp(5432), 'Allow ECS to RDS');

    // ========================================
    // RDS PostgreSQL (Orleans Clustering/State/Reminders)
    // ========================================
    const rdsSecret = new secretsmanager.Secret(this, 'RdsSecret', {
      secretName: `sekibandcbdecideraws-${envName}-rds`,
      generateSecretString: {
        secretStringTemplate: JSON.stringify({ username: 'orleans' }),
        generateStringKey: 'password',
        excludePunctuation: true,
      },
    });

    const rdsInstance = new rds.DatabaseInstance(this, 'RdsInstance', {
      engine: rds.DatabaseInstanceEngine.postgres({
        version: rds.PostgresEngineVersion.VER_16,
      }),
      instanceType: ec2.InstanceType.of(
        ec2.InstanceClass.T4G,
        config.rds.instanceClass === 'db.t4g.micro' ? ec2.InstanceSize.MICRO : ec2.InstanceSize.SMALL
      ),
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PRIVATE_WITH_EGRESS },
      securityGroups: [rdsSg],
      credentials: rds.Credentials.fromSecret(rdsSecret),
      databaseName: config.rds.databaseName,
      allocatedStorage: config.rds.allocatedStorage,
      multiAz: config.rds.multiAz,
      deletionProtection: envName === 'prod',
      removalPolicy: envName === 'prod' ? cdk.RemovalPolicy.RETAIN : cdk.RemovalPolicy.DESTROY,
    });

    // ========================================
    // RDS PostgreSQL (Identity/Auth - separate from Orleans)
    // ========================================
    const identityRdsSecret = new secretsmanager.Secret(this, 'IdentityRdsSecret', {
      secretName: `sekibandcbdecideraws-${envName}-identity-rds`,
      generateSecretString: {
        secretStringTemplate: JSON.stringify({ username: 'authuser' }),
        generateStringKey: 'password',
        excludePunctuation: true,
      },
    });

    const identityRdsInstance = new rds.DatabaseInstance(this, 'IdentityRdsInstance', {
      engine: rds.DatabaseInstanceEngine.postgres({
        version: rds.PostgresEngineVersion.VER_16,
      }),
      instanceType: ec2.InstanceType.of(
        ec2.InstanceClass.T4G,
        ec2.InstanceSize.MICRO  // Smaller instance for Identity DB
      ),
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PRIVATE_WITH_EGRESS },
      securityGroups: [rdsSg],
      credentials: rds.Credentials.fromSecret(identityRdsSecret),
      databaseName: 'authdb',
      allocatedStorage: 20,
      multiAz: false,
      deletionProtection: envName === 'prod',
      removalPolicy: envName === 'prod' ? cdk.RemovalPolicy.RETAIN : cdk.RemovalPolicy.DESTROY,
    });

    // ========================================
    // DynamoDB Tables
    // Tables are auto-created by the application with correct schema via DynamoDbContext.EnsureTablesAsync()
    // Here we just reference them by name for IAM permissions
    // ========================================
    const eventsTableName = config.dynamodb.eventsTableName;
    const projectionsTableName = `${config.dynamodb.eventsTableName}-projections`;
    const tagsTableName = `${config.dynamodb.eventsTableName}-tags`;

    // ========================================
    // SQS (Orleans Streams - reserved for future use)
    // ========================================
    const dlq = new sqs.Queue(this, 'OrleansDlq', {
      queueName: `${config.sqs.queueNamePrefix}-dlq`,
      retentionPeriod: cdk.Duration.days(14),
    });

    const sqsQueues: sqs.Queue[] = [];
    for (let i = 0; i < config.sqs.queueCount; i++) {
      const queue = new sqs.Queue(this, `OrleansQueue${i}`, {
        queueName: `${config.sqs.queueNamePrefix}-${i}`,
        visibilityTimeout: cdk.Duration.seconds(config.sqs.visibilityTimeoutSec),
        retentionPeriod: cdk.Duration.days(config.sqs.messageRetentionDays),
        deadLetterQueue: {
          queue: dlq,
          maxReceiveCount: 5,
        },
      });
      sqsQueues.push(queue);
    }

    // ========================================
    // S3 (Snapshots)
    // ========================================
    const snapshotBucket = new s3.Bucket(this, 'SnapshotBucket', {
      bucketName: `${config.s3.snapshotBucketName}-${this.account}`,
      encryption: s3.BucketEncryption.S3_MANAGED,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      removalPolicy: envName === 'prod' ? cdk.RemovalPolicy.RETAIN : cdk.RemovalPolicy.DESTROY,
      autoDeleteObjects: envName !== 'prod',
    });

    // ========================================
    // ECR Repositories (use existing or create new)
    // ========================================
    const apiEcrRepoName = `sekibandcbdecideraws-api-${envName}`;
    const webnextEcrRepoName = `sekibandcbdecideraws-webnext-${envName}`;

    // Try to use existing repositories first (for cases where images are pre-pushed)
    // If not found, CDK will create new ones
    const apiEcrRepo = ecr.Repository.fromRepositoryName(this, 'ApiEcrRepo', apiEcrRepoName);
    const webnextEcrRepo = ecr.Repository.fromRepositoryName(this, 'WebNextEcrRepo', webnextEcrRepoName);

    // ========================================
    // ECS Cluster
    // ========================================
    const cluster = new ecs.Cluster(this, 'Cluster', {
      vpc,
      clusterName: `sekibandcbdecideraws-${envName}`,
      containerInsights: true,
    });

    // Cloud Map namespace for service discovery
    const namespace = new servicediscovery.PrivateDnsNamespace(this, 'Namespace', {
      name: `sekibandcbdecideraws-${envName}.internal`,
      vpc,
    });

    // ========================================
    // Log Groups
    // ========================================
    const apiLogGroup = new logs.LogGroup(this, 'ApiLogGroup', {
      logGroupName: `/ecs/sekibandcbdecideraws-api-${envName}`,
      retention: logs.RetentionDays.ONE_WEEK,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    const webnextLogGroup = new logs.LogGroup(this, 'WebNextLogGroup', {
      logGroupName: `/ecs/sekibandcbdecideraws-webnext-${envName}`,
      retention: logs.RetentionDays.ONE_WEEK,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // ========================================
    // API Service Task Definition
    // ========================================
    const apiTaskDefinition = new ecs.FargateTaskDefinition(this, 'ApiTaskDef', {
      cpu: config.ecs.cpu,
      memoryLimitMiB: config.ecs.memory,
      runtimePlatform: {
        cpuArchitecture: ecs.CpuArchitecture.ARM64,
        operatingSystemFamily: ecs.OperatingSystemFamily.LINUX,
      },
    });

    // Grant DynamoDB permissions to API
    // Tables are auto-created by the app, so we grant permissions by table name pattern
    apiTaskDefinition.taskRole.addToPrincipalPolicy(new cdk.aws_iam.PolicyStatement({
      effect: cdk.aws_iam.Effect.ALLOW,
      actions: [
        'dynamodb:GetItem',
        'dynamodb:PutItem',
        'dynamodb:UpdateItem',
        'dynamodb:DeleteItem',
        'dynamodb:Query',
        'dynamodb:Scan',
        'dynamodb:BatchGetItem',
        'dynamodb:BatchWriteItem',
        'dynamodb:DescribeTable',
        'dynamodb:CreateTable',
        'dynamodb:TransactWriteItems',
      ],
      resources: [
        `arn:aws:dynamodb:${config.region}:${this.account}:table/${eventsTableName}`,
        `arn:aws:dynamodb:${config.region}:${this.account}:table/${eventsTableName}/index/*`,
        `arn:aws:dynamodb:${config.region}:${this.account}:table/${projectionsTableName}`,
        `arn:aws:dynamodb:${config.region}:${this.account}:table/${projectionsTableName}/index/*`,
        `arn:aws:dynamodb:${config.region}:${this.account}:table/${tagsTableName}`,
        `arn:aws:dynamodb:${config.region}:${this.account}:table/${tagsTableName}/index/*`,
      ],
    }));
    snapshotBucket.grantReadWrite(apiTaskDefinition.taskRole);
    rdsSecret.grantRead(apiTaskDefinition.taskRole);
    identityRdsSecret.grantRead(apiTaskDefinition.taskRole);
    sqsQueues.forEach(q => {
      q.grantSendMessages(apiTaskDefinition.taskRole);
      q.grantConsumeMessages(apiTaskDefinition.taskRole);
    });

    apiTaskDefinition.addContainer('ApiService', {
      image: ecs.ContainerImage.fromEcrRepository(apiEcrRepo, 'latest'),
      logging: ecs.LogDrivers.awsLogs({
        streamPrefix: 'api',
        logGroup: apiLogGroup,
      }),
      healthCheck: {
        command: ['CMD-SHELL', `curl -f http://localhost:${config.orleans.httpPort}/health || exit 1`],
        interval: cdk.Duration.seconds(30),
        timeout: cdk.Duration.seconds(10),
        retries: 10,
        startPeriod: cdk.Duration.seconds(300), // Give Orleans + RDS connection time to start
      },
      environment: {
        // Use 'Staging' for AWS dev environments to avoid loading appsettings.Development.json
        // which contains LocalStack-specific settings like DynamoDb:ServiceUrl
        'ASPNETCORE_ENVIRONMENT': envName === 'prod' ? 'Production' : 'Staging',
        'Sekiban__Database': 'dynamodb',
        'Orleans__UseInMemoryStreams': 'true',
        'Orleans__ClusterId': config.orleans.clusterId,
        'Orleans__ServiceId': config.orleans.serviceId,
        'Orleans__SiloPort': config.orleans.siloPort.toString(),
        'Orleans__GatewayPort': config.orleans.gatewayPort.toString(),
        'AWS__Region': config.region,
        'DynamoDb__EventsTableName': config.dynamodb.eventsTableName,
        'DynamoDb__TagsTableName': `${config.dynamodb.eventsTableName}-tags`,
        'DynamoDb__ProjectionStatesTableName': `${config.dynamodb.eventsTableName}-projections`,
        'S3BlobStorage__BucketName': snapshotBucket.bucketName,
        'Sqs__QueuePrefix': config.sqs.queueNamePrefix,
        'Sqs__QueueCount': config.sqs.queueCount.toString(),
      },
      secrets: {
        // Extract individual fields from RDS secret for connection string construction in app
        'RDS_HOST': ecs.Secret.fromSecretsManager(rdsSecret, 'host'),
        'RDS_PORT': ecs.Secret.fromSecretsManager(rdsSecret, 'port'),
        'RDS_USERNAME': ecs.Secret.fromSecretsManager(rdsSecret, 'username'),
        'RDS_PASSWORD': ecs.Secret.fromSecretsManager(rdsSecret, 'password'),
        'RDS_DATABASE': ecs.Secret.fromSecretsManager(rdsSecret, 'dbname'),
        // Identity RDS (separate database for authentication)
        'IDENTITY_RDS_HOST': ecs.Secret.fromSecretsManager(identityRdsSecret, 'host'),
        'IDENTITY_RDS_PORT': ecs.Secret.fromSecretsManager(identityRdsSecret, 'port'),
        'IDENTITY_RDS_USERNAME': ecs.Secret.fromSecretsManager(identityRdsSecret, 'username'),
        'IDENTITY_RDS_PASSWORD': ecs.Secret.fromSecretsManager(identityRdsSecret, 'password'),
        'IDENTITY_RDS_DATABASE': ecs.Secret.fromSecretsManager(identityRdsSecret, 'dbname'),
      },
      portMappings: [
        { containerPort: config.orleans.httpPort, name: 'http' },
        { containerPort: config.orleans.siloPort, name: 'silo' },
        { containerPort: config.orleans.gatewayPort, name: 'gateway' },
      ],
    });

    // ========================================
    // WebNext (Next.js) Service Task Definition
    // ========================================
    const webnextTaskDefinition = new ecs.FargateTaskDefinition(this, 'WebNextTaskDef', {
      cpu: config.webnext.cpu,
      memoryLimitMiB: config.webnext.memory,
      runtimePlatform: {
        cpuArchitecture: ecs.CpuArchitecture.ARM64,
        operatingSystemFamily: ecs.OperatingSystemFamily.LINUX,
      },
    });

    // API service URL via Cloud Map (internal HTTP communication)
    const apiServiceUrl = `http://apiservice.sekibandcbdecideraws-${envName}.internal:${config.orleans.httpPort}`;

    webnextTaskDefinition.addContainer('WebNextService', {
      image: ecs.ContainerImage.fromEcrRepository(webnextEcrRepo, 'latest'),
      logging: ecs.LogDrivers.awsLogs({
        streamPrefix: 'webnext',
        logGroup: webnextLogGroup,
      }),
      environment: {
        'NODE_ENV': envName === 'prod' ? 'production' : 'production',
        'PORT': config.webnext.httpPort.toString(),
        // API endpoint for tRPC/BFF
        'API_BASE_URL': apiServiceUrl,
      },
      portMappings: [
        { containerPort: config.webnext.httpPort, name: 'http' },
      ],
    });

    // ========================================
    // External ALB (WebNext only - API is internal only)
    // ========================================
    const alb = new elbv2.ApplicationLoadBalancer(this, 'Alb', {
      vpc,
      internetFacing: true,
      securityGroup: albSg,
    });

    // WebNext Target Group
    const webnextTargetGroup = new elbv2.ApplicationTargetGroup(this, 'WebNextTargetGroup', {
      vpc,
      port: config.webnext.httpPort,
      protocol: elbv2.ApplicationProtocol.HTTP,
      targetType: elbv2.TargetType.IP,
      healthCheck: {
        path: '/',
        interval: cdk.Duration.seconds(30),
        timeout: cdk.Duration.seconds(5),
        healthyThresholdCount: 2,
        unhealthyThresholdCount: 3,
      },
    });

    // Configure external ALB listeners based on HTTPS configuration
    if (httpsEnabled) {
      // Import certificate
      const certificate = acm.Certificate.fromCertificateArn(
        this, 'Certificate', config.alb.certificateArn
      );

      // HTTPS Listener - all traffic to WebNext
      alb.addListener('HttpsListener', {
        port: 443,
        protocol: elbv2.ApplicationProtocol.HTTPS,
        certificates: [certificate],
        defaultAction: elbv2.ListenerAction.forward([webnextTargetGroup]),
      });

      // HTTP to HTTPS redirect
      alb.addListener('HttpListener', {
        port: 80,
        defaultAction: elbv2.ListenerAction.redirect({
          protocol: 'HTTPS',
          port: '443',
          permanent: true,
        }),
      });
    } else {
      // HTTP only (when no certificate configured) - all traffic to WebNext
      alb.addListener('HttpListener', {
        port: 80,
        defaultAction: elbv2.ListenerAction.forward([webnextTargetGroup]),
      });
    }

    // ========================================
    // API ECS Service (internal only - accessible via Cloud Map)
    // ========================================
    const apiService = new ecs.FargateService(this, 'ApiService', {
      cluster,
      taskDefinition: apiTaskDefinition,
      desiredCount: config.ecs.desiredCount,
      securityGroups: [ecsSg],
      vpcSubnets: { subnetType: ec2.SubnetType.PRIVATE_WITH_EGRESS },
      cloudMapOptions: {
        name: 'apiservice',
        cloudMapNamespace: namespace,
        dnsRecordType: servicediscovery.DnsRecordType.A,
        dnsTtl: cdk.Duration.seconds(10),
      },
      circuitBreaker: { rollback: false },  // Temporarily disabled for debugging
    });

    // Ensure RDS instances are fully created before ECS service starts
    // This prevents tasks from failing due to missing RDS endpoint
    apiService.node.addDependency(rdsInstance);
    apiService.node.addDependency(identityRdsInstance);

    // API service is internal only - no external ALB attachment
    // Access via Cloud Map: apiservice.sekibandcbdecideraws-{env}.internal

    // API Auto Scaling
    const apiScaling = apiService.autoScaleTaskCount({
      minCapacity: config.ecs.minCount,
      maxCapacity: config.ecs.maxCount,
    });

    apiScaling.scaleOnCpuUtilization('ApiCpuScaling', {
      targetUtilizationPercent: 70,
    });

    // ========================================
    // WebNext ECS Service
    // ========================================
    const webnextService = new ecs.FargateService(this, 'WebNextService', {
      cluster,
      taskDefinition: webnextTaskDefinition,
      desiredCount: config.webnext.desiredCount,
      securityGroups: [ecsSg],
      vpcSubnets: { subnetType: ec2.SubnetType.PRIVATE_WITH_EGRESS },
      cloudMapOptions: {
        name: 'webnextservice',
        cloudMapNamespace: namespace,
        dnsRecordType: servicediscovery.DnsRecordType.A,
        dnsTtl: cdk.Duration.seconds(10),
      },
      circuitBreaker: { rollback: true },
    });

    webnextService.attachToApplicationTargetGroup(webnextTargetGroup);

    // WebNext Auto Scaling
    const webnextScaling = webnextService.autoScaleTaskCount({
      minCapacity: config.webnext.minCount,
      maxCapacity: config.webnext.maxCount,
    });

    webnextScaling.scaleOnCpuUtilization('WebNextCpuScaling', {
      targetUtilizationPercent: 70,
    });

    // ========================================
    // CloudFront Distribution (HTTPS frontend)
    // Using CloudFront default domain (*.cloudfront.net) for HTTPS
    // This avoids needing a custom domain and ACM certificate
    // ========================================
    const distribution = new cloudfront.Distribution(this, 'Distribution', {
      defaultBehavior: {
        origin: new origins.HttpOrigin(alb.loadBalancerDnsName, {
          protocolPolicy: cloudfront.OriginProtocolPolicy.HTTP_ONLY,
          httpPort: 80,
        }),
        viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        allowedMethods: cloudfront.AllowedMethods.ALLOW_ALL,
        cachePolicy: cloudfront.CachePolicy.CACHING_DISABLED,
        originRequestPolicy: cloudfront.OriginRequestPolicy.ALL_VIEWER,
      },
      // Use PriceClass_100 for lowest cost (US, Canada, Europe only)
      // Change to PriceClass_200 if you need Asia/Pacific edge locations
      priceClass: cloudfront.PriceClass.PRICE_CLASS_200,
      comment: `SekibanDcbDeciderAws ${envName} - CloudFront Distribution`,
    });

    // ========================================
    // Outputs
    // ========================================
    new cdk.CfnOutput(this, 'CloudFrontUrl', {
      value: `https://${distribution.distributionDomainName}`,
      description: 'CloudFront HTTPS URL (use this for public access)',
    });

    new cdk.CfnOutput(this, 'CloudFrontDistributionId', {
      value: distribution.distributionId,
      description: 'CloudFront Distribution ID',
    });

    new cdk.CfnOutput(this, 'AlbDnsName', {
      value: alb.loadBalancerDnsName,
      description: 'ALB DNS Name (internal, use CloudFront URL instead)',
    });

    new cdk.CfnOutput(this, 'InternalApiEndpoint', {
      value: `http://apiservice.sekibandcbdecideraws-${envName}.internal:${config.orleans.httpPort}`,
      description: 'Internal API Endpoint (Cloud Map)',
    });

    new cdk.CfnOutput(this, 'ApiEcrRepositoryUri', {
      value: apiEcrRepo.repositoryUri,
      description: 'API ECR Repository URI',
    });

    new cdk.CfnOutput(this, 'WebNextEcrRepositoryUri', {
      value: webnextEcrRepo.repositoryUri,
      description: 'WebNext ECR Repository URI',
    });

    new cdk.CfnOutput(this, 'RdsEndpoint', {
      value: rdsInstance.instanceEndpoint.hostname,
      description: 'RDS Endpoint (Orleans)',
    });

    new cdk.CfnOutput(this, 'IdentityRdsEndpoint', {
      value: identityRdsInstance.instanceEndpoint.hostname,
      description: 'RDS Endpoint (Identity)',
    });

    new cdk.CfnOutput(this, 'EventsTableName', {
      value: eventsTableName,
      description: 'DynamoDB Events Table Name',
    });

    new cdk.CfnOutput(this, 'SnapshotBucketName', {
      value: snapshotBucket.bucketName,
      description: 'S3 Snapshot Bucket Name',
    });

    new cdk.CfnOutput(this, 'ClusterName', {
      value: cluster.clusterName,
      description: 'ECS Cluster Name',
    });

    new cdk.CfnOutput(this, 'ApiServiceName', {
      value: apiService.serviceName,
      description: 'API ECS Service Name',
    });

    new cdk.CfnOutput(this, 'WebNextServiceName', {
      value: webnextService.serviceName,
      description: 'WebNext ECS Service Name',
    });
  }
}

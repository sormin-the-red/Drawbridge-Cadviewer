import * as cdk from 'aws-cdk-lib';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as apigw from 'aws-cdk-lib/aws-apigateway';
import * as cognito from 'aws-cdk-lib/aws-cognito';
import * as secretsmanager from 'aws-cdk-lib/aws-secretsmanager';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as path from 'path';
import { Construct } from 'constructs';

export class DrawbridgeStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // ── Parameters ────────────────────────────────────────────────────────────

    const apsClientId = new cdk.CfnParameter(this, 'ApsClientId', {
      type: 'String', noEcho: true, default: '',
      description: 'Autodesk Platform Services client ID',
    });

    const apsClientSecret = new cdk.CfnParameter(this, 'ApsClientSecret', {
      type: 'String', noEcho: true, default: '',
      description: 'Autodesk Platform Services client secret',
    });

    // Google OAuth credentials stored in Secrets Manager rather than CloudFormation
    // parameters to avoid import restrictions on externally-managed resources.
    const googleSecret = secretsmanager.Secret.fromSecretNameV2(
      this, 'GoogleSecret', 'drawbridge/google-oauth');

    // ── S3 ────────────────────────────────────────────────────────────────────

    const drawingsBucket = new s3.Bucket(this, 'DrawingsBucket', {
      bucketName: 'drawbridge-drawings-prod',
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      encryption: s3.BucketEncryption.S3_MANAGED,
      cors: [{
        allowedMethods: [s3.HttpMethods.GET],
        allowedOrigins: ['*'],
        allowedHeaders: ['*'],
        maxAge: 3600,
      }],
    });

    const spaBucket = new s3.Bucket(this, 'SpaBucket', {
      bucketName: 'drawbridge-spa-prod',
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      encryption: s3.BucketEncryption.S3_MANAGED,
    });

    // ── SQS ───────────────────────────────────────────────────────────────────

    const jobsDlq = new sqs.Queue(this, 'JobsDLQ', {
      queueName: 'drawbridge-jobs-dlq-prod',
      retentionPeriod: cdk.Duration.days(14),
    });

    const jobsQueue = new sqs.Queue(this, 'JobsQueue', {
      queueName: 'drawbridge-jobs-prod',
      visibilityTimeout: cdk.Duration.hours(1),
      retentionPeriod: cdk.Duration.days(1),
      deadLetterQueue: { queue: jobsDlq, maxReceiveCount: 2 },
    });

    // ── DynamoDB ──────────────────────────────────────────────────────────────

    const productsTable = new dynamodb.Table(this, 'ProductsTable', {
      tableName: 'drawbridge-products-prod',
      partitionKey: { name: 'partNumber', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });

    const versionsTable = new dynamodb.Table(this, 'VersionsTable', {
      tableName: 'drawbridge-versions-prod',
      partitionKey: { name: 'partNumber', type: dynamodb.AttributeType.STRING },
      sortKey:      { name: 'version',    type: dynamodb.AttributeType.NUMBER },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });

    const jobsTable = new dynamodb.Table(this, 'JobsTable', {
      tableName: 'drawbridge-jobs-prod',
      partitionKey: { name: 'jobId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      timeToLiveAttribute: 'ttl',
    });

    const annotationsTable = new dynamodb.Table(this, 'AnnotationsTable', {
      tableName: 'drawbridge-annotations-prod',
      partitionKey: { name: 'annotationId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });

    annotationsTable.addGlobalSecondaryIndex({
      indexName: 'ByVersion',
      partitionKey: { name: 'versionKey', type: dynamodb.AttributeType.STRING },
      sortKey:      { name: 'createdAt',  type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });

    const markupsTable = new dynamodb.Table(this, 'MarkupsTable', {
      tableName: 'drawbridge-markups-prod',
      partitionKey: { name: 'markupId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });

    markupsTable.addGlobalSecondaryIndex({
      indexName: 'ByVersion',
      partitionKey: { name: 'versionKey', type: dynamodb.AttributeType.STRING },
      sortKey:      { name: 'createdAt',  type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });

    // ── CloudFront ────────────────────────────────────────────────────────────
    // Both distributions use OAC (SigV4) — the older OAI approach is deprecated.

    const drawingsOac = new cloudfront.CfnOriginAccessControl(this, 'DrawingsOAC', {
      originAccessControlConfig: {
        name: 'drawbridge-drawings-oac-prod',
        originAccessControlOriginType: 's3',
        signingBehavior: 'always',
        signingProtocol: 'sigv4',
      },
    });

    const drawingsCorsPolicy = new cloudfront.CfnResponseHeadersPolicy(this, 'DrawingsCorsPolicy', {
      responseHeadersPolicyConfig: {
        name: 'drawbridge-drawings-cors-prod',
        corsConfig: {
          accessControlAllowCredentials: false,
          accessControlAllowHeaders: { items: ['*'] },
          accessControlAllowMethods: { items: ['GET', 'HEAD'] },
          accessControlAllowOrigins: { items: ['*'] },
          originOverride: true,
        },
      },
    });

    const drawingsCfnDist = new cloudfront.CfnDistribution(this, 'DrawingsDistribution', {
      distributionConfig: {
        enabled: true,
        defaultCacheBehavior: {
          targetOriginId: 'DrawingsOrigin',
          viewerProtocolPolicy: 'redirect-to-https',
          cachePolicyId: '658327ea-f89d-4fab-a63d-7e88639e58f6', // CachingOptimized
          allowedMethods: ['GET', 'HEAD'],
          responseHeadersPolicyId: drawingsCorsPolicy.ref,
        },
        origins: [{
          id: 'DrawingsOrigin',
          domainName: drawingsBucket.bucketRegionalDomainName,
          originAccessControlId: drawingsOac.ref,
          s3OriginConfig: {},
        }],
      },
    });

    drawingsBucket.addToResourcePolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      principals: [new iam.ServicePrincipal('cloudfront.amazonaws.com')],
      actions: ['s3:GetObject'],
      resources: [drawingsBucket.arnForObjects('*')],
      conditions: {
        StringEquals: {
          'AWS:SourceArn': `arn:aws:cloudfront::${this.account}:distribution/${drawingsCfnDist.ref}`,
        },
      },
    }));

    const drawingsDomainName = drawingsCfnDist.attrDomainName;

    const spaOac = new cloudfront.CfnOriginAccessControl(this, 'SpaOAC', {
      originAccessControlConfig: {
        name: 'drawbridge-spa-oac-prod',
        originAccessControlOriginType: 's3',
        signingBehavior: 'always',
        signingProtocol: 'sigv4',
      },
    });

    const spaCfnDist = new cloudfront.CfnDistribution(this, 'SpaDistribution', {
      distributionConfig: {
        enabled: true,
        defaultRootObject: 'index.html',
        // Rewrite 403/404 to index.html so Angular's pushState routing works.
        customErrorResponses: [
          { errorCode: 403, responseCode: 200, responsePagePath: '/index.html' },
          { errorCode: 404, responseCode: 200, responsePagePath: '/index.html' },
        ],
        defaultCacheBehavior: {
          targetOriginId: 'SpaOrigin',
          viewerProtocolPolicy: 'redirect-to-https',
          cachePolicyId: '658327ea-f89d-4fab-a63d-7e88639e58f6',
          allowedMethods: ['GET', 'HEAD'],
        },
        origins: [{
          id: 'SpaOrigin',
          domainName: spaBucket.bucketRegionalDomainName,
          originAccessControlId: spaOac.ref,
          s3OriginConfig: {},
        }],
      },
    });

    spaBucket.addToResourcePolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      principals: [new iam.ServicePrincipal('cloudfront.amazonaws.com')],
      actions: ['s3:GetObject'],
      resources: [spaBucket.arnForObjects('*')],
      conditions: {
        StringEquals: {
          'AWS:SourceArn': `arn:aws:cloudfront::${this.account}:distribution/${spaCfnDist.ref}`,
        },
      },
    }));

    const spaDomainName = spaCfnDist.attrDomainName;

    // ── Cognito ───────────────────────────────────────────────────────────────

    const userPool = new cognito.UserPool(this, 'UserPool', {
      userPoolName: 'drawbridge-prod',
      signInAliases: { email: true },
      autoVerify: { email: true },
      standardAttributes: {
        email: { required: true, mutable: true },
      },
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });

    const userPoolDomain = new cognito.UserPoolDomain(this, 'UserPoolDomain', {
      userPool,
      cognitoDomain: { domainPrefix: 'drawbridge-prod' },
    });

    const googleIdP = new cognito.UserPoolIdentityProviderGoogle(this, 'GoogleIdP', {
      userPool,
      clientId: googleSecret.secretValueFromJson('clientId').unsafeUnwrap(),
      clientSecretValue: googleSecret.secretValueFromJson('clientSecret'),
      scopes: ['openid', 'email', 'profile'],
      attributeMapping: {
        email:          cognito.ProviderAttribute.GOOGLE_EMAIL,
        fullname:       cognito.ProviderAttribute.GOOGLE_NAME,
        profilePicture: cognito.ProviderAttribute.GOOGLE_PICTURE,
      },
    });

    const userPoolClient = new cognito.UserPoolClient(this, 'UserPoolClient', {
      userPool,
      userPoolClientName: 'drawbridge-spa-prod',
      generateSecret: false,
      supportedIdentityProviders: [
        cognito.UserPoolClientIdentityProvider.custom('Google'),
      ],
      oAuth: {
        flows: { authorizationCodeGrant: true },
        scopes: [
          cognito.OAuthScope.OPENID,
          cognito.OAuthScope.EMAIL,
          cognito.OAuthScope.PROFILE,
        ],
        callbackUrls: [
          'http://localhost:4200/auth/callback',
          cdk.Fn.join('', ['https://', spaDomainName, '/auth/callback']),
        ],
        logoutUrls: [
          'http://localhost:4200',
          cdk.Fn.join('', ['https://', spaDomainName]),
        ],
      },
      authFlows: { userPassword: false, userSrp: false, adminUserPassword: false },
      idTokenValidity: cdk.Duration.hours(8),
      accessTokenValidity: cdk.Duration.hours(8),
      refreshTokenValidity: cdk.Duration.days(30),
      preventUserExistenceErrors: true,
    });
    userPoolClient.node.addDependency(googleIdP);

    // ── Lambda (API) ──────────────────────────────────────────────────────────
    // net10.0 self-contained publish targets provided.al2023 (custom runtime).
    // Build: dotnet publish src/Drawbridge.Api -c Release -r linux-x64
    //        --self-contained true -o infra/lambda-pkg/api && zip -j infra/lambda-pkg/api.zip ...
    const apiFunction = new lambda.Function(this, 'ApiFunction', {
      runtime: lambda.Runtime.PROVIDED_AL2023,
      handler: 'bootstrap',
      code: lambda.Code.fromAsset(path.join(__dirname, '../lambda-pkg/api.zip')),
      memorySize: 512,
      timeout: cdk.Duration.seconds(30),
      environment: {
        Api__AwsRegion:         this.region,
        Api__SqsQueueUrl:       jobsQueue.queueUrl,
        Api__ProductsTable:     productsTable.tableName,
        Api__VersionsTable:     versionsTable.tableName,
        Api__JobsTable:         jobsTable.tableName,
        Api__AnnotationsTable:  annotationsTable.tableName,
        Api__MarkupsTable:      markupsTable.tableName,
        Api__S3Bucket:          drawingsBucket.bucketName,
        Api__CloudFrontBaseUrl: cdk.Fn.join('', ['https://', drawingsDomainName]),
        Api__ApsClientId:       apsClientId.valueAsString,
        Api__ApsClientSecret:   apsClientSecret.valueAsString,
        Api__CognitoUserPoolId: userPool.userPoolId,
        Api__CognitoClientId:   userPoolClient.userPoolClientId,
      },
    });

    jobsQueue.grantSendMessages(apiFunction);
    productsTable.grantReadWriteData(apiFunction);
    versionsTable.grantReadWriteData(apiFunction);
    jobsTable.grantReadWriteData(apiFunction);
    annotationsTable.grantReadWriteData(apiFunction);
    markupsTable.grantReadWriteData(apiFunction);
    drawingsBucket.grantPut(apiFunction, 'products/*/markups/*');
    drawingsBucket.grantDelete(apiFunction, 'products/*/markups/*');
    apiFunction.addToRolePolicy(new iam.PolicyStatement({
      actions:   ['cognito-idp:ListUsers'],
      resources: [userPool.userPoolArn],
    }));

    // ── API Gateway ───────────────────────────────────────────────────────────

    const api = new apigw.LambdaRestApi(this, 'Api', {
      handler: apiFunction,
      proxy: true,
      deployOptions: { stageName: 'Prod' },
    });

    // ── IAM — ConversionWorker user ───────────────────────────────────────────

    const workerUser = new iam.User(this, 'ConversionWorkerUser', {
      userName: 'drawbridge-conversion-worker-prod',
    });

    workerUser.addToPolicy(new iam.PolicyStatement({
      actions: ['sqs:ReceiveMessage', 'sqs:DeleteMessage', 'sqs:ChangeMessageVisibility'],
      resources: [jobsQueue.queueArn],
    }));

    workerUser.addToPolicy(new iam.PolicyStatement({
      actions: ['s3:PutObject', 's3:PutObjectAcl'],
      resources: [drawingsBucket.arnForObjects('products/*')],
    }));

    workerUser.addToPolicy(new iam.PolicyStatement({
      actions: ['dynamodb:UpdateItem'],
      resources: [jobsTable.tableArn],
    }));

    workerUser.addToPolicy(new iam.PolicyStatement({
      actions: ['dynamodb:PutItem', 'dynamodb:GetItem', 'dynamodb:UpdateItem'],
      resources: [productsTable.tableArn, versionsTable.tableArn],
    }));

    // ── Outputs ───────────────────────────────────────────────────────────────

    new cdk.CfnOutput(this, 'ApiUrl', {
      description: 'API Gateway invoke URL',
      value: api.url,
    });
    new cdk.CfnOutput(this, 'SpaDistributionDomain', {
      description: 'SPA CloudFront domain — add /auth/callback to Cognito + Google Cloud Console',
      value: cdk.Fn.join('', ['https://', spaDomainName]),
    });
    new cdk.CfnOutput(this, 'DrawingsDistributionDomain', {
      description: 'Drawings CloudFront domain — set Api__CloudFrontBaseUrl',
      value: cdk.Fn.join('', ['https://', drawingsDomainName]),
    });
    new cdk.CfnOutput(this, 'CognitoUserPoolId', { value: userPool.userPoolId });
    new cdk.CfnOutput(this, 'CognitoClientId', { value: userPoolClient.userPoolClientId });
    new cdk.CfnOutput(this, 'CognitoDomain', { value: userPoolDomain.baseUrl() });
    new cdk.CfnOutput(this, 'GoogleIdpRedirectUri', {
      description: 'Register in Google Cloud Console → Authorized redirect URIs',
      value: `https://drawbridge-prod.auth.${this.region}.amazoncognito.com/oauth2/idpresponse`,
    });
    new cdk.CfnOutput(this, 'JobsQueueUrl', { value: jobsQueue.queueUrl });
    new cdk.CfnOutput(this, 'DrawingsBucketName', { value: drawingsBucket.bucketName });
    new cdk.CfnOutput(this, 'SpaBucketName', { value: spaBucket.bucketName });
    new cdk.CfnOutput(this, 'ConversionWorkerUserName', { value: workerUser.userName });
  }
}

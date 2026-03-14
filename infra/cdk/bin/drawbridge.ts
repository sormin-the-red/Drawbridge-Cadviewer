#!/usr/bin/env node
import 'source-map-support/register';
import * as cdk from 'aws-cdk-lib';
import { DrawbridgeStack } from '../lib/drawbridge-stack';

const app = new cdk.App();

const stack = new DrawbridgeStack(app, 'DrawbridgeStack', {
  env: {
    account: process.env.CDK_DEFAULT_ACCOUNT,
    region: 'us-east-2',
  },
});

cdk.Tags.of(stack).add('Project', 'Drawbridge');
cdk.Tags.of(stack).add('ManagedBy', 'CDK');
cdk.Tags.of(stack).add('Environment', 'prod');

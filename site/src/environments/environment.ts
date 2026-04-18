export const environment = {
  production: false,
  apiBaseUrl: 'https://API_GATEWAY_ID.execute-api.us-east-2.amazonaws.com/Prod',
  cognito: {
    domain: 'YOUR_COGNITO_DOMAIN.auth.us-east-2.amazoncognito.com',
    clientId: 'YOUR_COGNITO_CLIENT_ID',
    redirectUri: 'http://localhost:4200/auth/callback',
  },
};

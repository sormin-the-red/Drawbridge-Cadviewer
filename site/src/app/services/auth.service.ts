import { Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '../../environments/environment';

interface TokenPayload {
  sub: string;
  email: string;
  exp: number;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly TOKEN_KEY    = 'db_id_token';
  private readonly REFRESH_KEY  = 'db_refresh_token';
  private readonly EXPIRY_KEY   = 'db_token_expiry';
  private readonly VERIFIER_KEY = 'db_pkce_verifier';

  readonly isAuthenticated = signal(this.hasValidToken());

  private refreshPromise: Promise<void> | null = null;

  constructor(private router: Router) {}

  async login(): Promise<void> {
    const verifier  = this.generateVerifier();
    const challenge = await this.sha256Base64Url(verifier);
    localStorage.setItem(this.VERIFIER_KEY, verifier);
    const params = new URLSearchParams({
      response_type:         'code',
      client_id:             environment.cognito.clientId,
      redirect_uri:          environment.cognito.redirectUri,
      scope:                 'openid email profile',
      code_challenge:        challenge,
      code_challenge_method: 'S256',
    });
    window.location.href = `https://${environment.cognito.domain}/login?${params}`;
  }

  async handleCallback(code: string): Promise<void> {
    const verifier = localStorage.getItem(this.VERIFIER_KEY) ?? '';
    localStorage.removeItem(this.VERIFIER_KEY);
    const res = await fetch(`https://${environment.cognito.domain}/oauth2/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type:    'authorization_code',
        client_id:     environment.cognito.clientId,
        redirect_uri:  environment.cognito.redirectUri,
        code,
        code_verifier: verifier,
      }),
    });
    if (!res.ok) throw new Error(`Token exchange failed: ${res.status}`);
    const data = await res.json();
    this.storeTokens(data.id_token, data.refresh_token, data.expires_in);
  }

  async getIdToken(): Promise<string | null> {
    const expiry = Number(localStorage.getItem(this.EXPIRY_KEY) ?? '0');
    if (Date.now() < expiry - 60_000) {
      return localStorage.getItem(this.TOKEN_KEY);
    }
    await this.refresh();
    return localStorage.getItem(this.TOKEN_KEY);
  }

  getCurrentUser(): { email: string; sub: string } | null {
    const token = localStorage.getItem(this.TOKEN_KEY);
    if (!token) return null;
    try {
      const payload = JSON.parse(atob(token.split('.')[1])) as TokenPayload;
      return { email: payload.email, sub: payload.sub };
    } catch {
      return null;
    }
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_KEY);
    localStorage.removeItem(this.EXPIRY_KEY);
    this.isAuthenticated.set(false);
    this.router.navigate(['/landing']);
  }

  async refresh(): Promise<void> {
    if (this.refreshPromise) return this.refreshPromise;
    this.refreshPromise = this.doRefresh().finally(() => { this.refreshPromise = null; });
    return this.refreshPromise;
  }

  private async doRefresh(): Promise<void> {
    const refreshToken = localStorage.getItem(this.REFRESH_KEY);
    if (!refreshToken) { this.logout(); return; }
    try {
      const res = await fetch(`https://${environment.cognito.domain}/oauth2/token`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          grant_type:    'refresh_token',
          client_id:     environment.cognito.clientId,
          refresh_token: refreshToken,
        }),
      });
      if (!res.ok) throw new Error('Refresh failed');
      const data = await res.json();
      this.storeTokens(data.id_token, data.refresh_token ?? refreshToken, data.expires_in);
    } catch {
      this.logout();
    }
  }

  private storeTokens(idToken: string, refreshToken: string, expiresIn: number): void {
    localStorage.setItem(this.TOKEN_KEY,   idToken);
    localStorage.setItem(this.REFRESH_KEY, refreshToken);
    localStorage.setItem(this.EXPIRY_KEY,  String(Date.now() + expiresIn * 1000));
    this.isAuthenticated.set(true);
  }

  private hasValidToken(): boolean {
    const expiry = Number(localStorage.getItem(this.EXPIRY_KEY) ?? '0');
    return !!localStorage.getItem(this.TOKEN_KEY) && Date.now() < expiry;
  }

  private generateVerifier(): string {
    const bytes = new Uint8Array(48);
    crypto.getRandomValues(bytes);
    return btoa(String.fromCharCode(...bytes))
      .replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
  }

  private async sha256Base64Url(plain: string): Promise<string> {
    const data = new TextEncoder().encode(plain);
    const hash = await crypto.subtle.digest('SHA-256', data);
    return btoa(String.fromCharCode(...new Uint8Array(hash)))
      .replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
  }
}

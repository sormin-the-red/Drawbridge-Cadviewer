import { Component, inject, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-auth-callback',
  imports: [RouterLink],
  templateUrl: './auth-callback.html',
})
export class AuthCallbackComponent implements OnInit {
  private auth   = inject(AuthService);
  private route  = inject(ActivatedRoute);
  private router = inject(Router);

  protected error: string | null = null;

  async ngOnInit(): Promise<void> {
    const code = this.route.snapshot.queryParamMap.get('code');
    if (!code) { this.router.navigate(['/landing']); return; }
    try {
      await this.auth.handleCallback(code);
      this.router.navigate(['/']);
    } catch (e) {
      this.error = e instanceof Error ? e.message : 'Authentication failed';
    }
  }
}

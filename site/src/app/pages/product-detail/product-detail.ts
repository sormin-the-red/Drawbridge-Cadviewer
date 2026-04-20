import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService } from '../../services/api.service';
import type { ProductDetail, VersionDetail } from '../../models/api.models';

@Component({
  selector: 'app-product-detail',
  imports: [RouterLink],
  templateUrl: './product-detail.html',
  styleUrl: './product-detail.scss',
})
export class ProductDetailComponent implements OnInit {
  private api    = inject(ApiService);
  private route  = inject(ActivatedRoute);
  private router = inject(Router);

  protected product    = signal<ProductDetail | null>(null);
  protected loading    = signal(true);
  protected error      = signal<string | null>(null);
  protected partNumber = '';

  async ngOnInit(): Promise<void> {
    this.partNumber = this.route.snapshot.paramMap.get('pn') ?? '';
    if (!this.partNumber) { this.router.navigate(['/']); return; }
    try {
      const p = await this.api.getProduct(this.partNumber);
      this.product.set(p);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Failed to load product');
    } finally {
      this.loading.set(false);
    }
  }

  protected statusClass(status: string): string {
    switch (status.toLowerCase()) {
      case 'complete': return 'status-complete';
      case 'failed':   return 'status-failed';
      case 'queued':   return 'status-queued';
      case 'running':  return 'status-running';
      default:         return 'status-other';
    }
  }

  protected canView(v: VersionDetail): boolean {
    return v.status.toLowerCase() === 'complete' &&
      (v.apsUrn != null || v.configUrns != null);
  }

  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString(undefined, {
      year: 'numeric', month: 'short', day: 'numeric',
    });
  }
}

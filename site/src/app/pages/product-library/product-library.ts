import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../services/api.service';
import type { ProductSummary } from '../../models/api.models';

@Component({
  selector: 'app-product-library',
  imports: [RouterLink],
  templateUrl: './product-library.html',
  styleUrl: './product-library.scss',
})
export class ProductLibraryComponent implements OnInit {
  private api = inject(ApiService);

  protected products = signal<ProductSummary[]>([]);
  protected loading  = signal(true);
  protected error    = signal<string | null>(null);
  protected search   = signal('');

  protected filtered = computed(() => {
    const q = this.search().toLowerCase();
    if (!q) return this.products();
    return this.products().filter(
      p =>
        p.partNumber.toLowerCase().includes(q) ||
        p.name.toLowerCase().includes(q) ||
        p.description.toLowerCase().includes(q),
    );
  });

  async ngOnInit(): Promise<void> {
    try {
      const list = await this.api.getProducts();
      this.products.set(list);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Failed to load products');
    } finally {
      this.loading.set(false);
    }
  }

  protected onSearch(e: Event): void {
    this.search.set((e.target as HTMLInputElement).value);
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
}

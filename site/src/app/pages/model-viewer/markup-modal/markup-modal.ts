import {
  Component,
  EventEmitter,
  HostListener,
  Input,
  OnChanges,
  Output,
  signal,
} from '@angular/core';
import { ApiService } from '../../../services/api.service';
import { inject } from '@angular/core';
import type { MarkupData, MarkupRecord } from '../../../models/api.models';

@Component({
  selector: 'app-markup-modal',
  templateUrl: './markup-modal.html',
  styleUrl: './markup-modal.scss',
})
export class MarkupModalComponent implements OnChanges {
  @Input()  markups: MarkupRecord[] = [];
  @Input()  initialIndex = 0;
  @Output() close = new EventEmitter<void>();
  @Output() restoreView = new EventEmitter<unknown>();

  private api = inject(ApiService);

  protected current = signal<MarkupRecord | null>(null);
  protected data    = signal<MarkupData | null>(null);
  protected index   = signal(0);
  protected loading = signal(false);

  ngOnChanges(): void {
    this.index.set(this.initialIndex);
    this.loadCurrent();
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (e.key === 'ArrowRight') this.next();
    else if (e.key === 'ArrowLeft') this.prev();
    else if (e.key === 'Escape') this.close.emit();
  }

  protected next(): void {
    if (this.index() < this.markups.length - 1) {
      this.index.update(i => i + 1);
      this.loadCurrent();
    }
  }

  protected prev(): void {
    if (this.index() > 0) {
      this.index.update(i => i - 1);
      this.loadCurrent();
    }
  }

  protected restore(): void {
    const d = this.data();
    if (d) this.restoreView.emit(d.viewerState);
  }

  private async loadCurrent(): Promise<void> {
    const m = this.markups[this.index()];
    if (!m) return;
    this.current.set(m);
    this.data.set(null);
    this.loading.set(true);
    try {
      const d = await this.api.getMarkupData(m.dataUrl);
      this.data.set(d);
    } catch { /* non-fatal */ }
    finally { this.loading.set(false); }
  }
}

import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import type { MarkupRecord } from '../../../models/api.models';

@Component({
  selector: 'app-markup-panel',
  templateUrl: './markup-panel.html',
  styleUrl: './markup-panel.scss',
})
export class MarkupPanelComponent {
  @Input()  markups: MarkupRecord[] = [];
  @Output() view    = new EventEmitter<MarkupRecord>();
  @Output() delete  = new EventEmitter<string>();
  @Output() reorder = new EventEmitter<{ markupId: string; sortOrder: number }>();

  protected draggedId = signal<string | null>(null);

  protected onDragStart(e: DragEvent, id: string): void {
    this.draggedId.set(id);
    e.dataTransfer!.effectAllowed = 'move';
  }

  protected onDragOver(e: DragEvent, target: MarkupRecord): void {
    e.preventDefault();
    const draggedId = this.draggedId();
    if (!draggedId || draggedId === target.markupId) return;
    const from = this.markups.findIndex(m => m.markupId === draggedId);
    const to   = this.markups.findIndex(m => m.markupId === target.markupId);
    if (from === -1 || to === -1) return;
    const reordered = [...this.markups];
    const [item] = reordered.splice(from, 1);
    reordered.splice(to, 0, item);
    // Emit new sort orders
    reordered.forEach((m, i) => {
      if (m.sortOrder !== i) this.reorder.emit({ markupId: m.markupId, sortOrder: i });
    });
  }

  protected onDragEnd(): void {
    this.draggedId.set(null);
  }

  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }
}

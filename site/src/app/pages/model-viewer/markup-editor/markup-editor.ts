import {
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  signal,
} from '@angular/core';

type Tool = 'pen' | 'eraser' | 'arrow' | 'circle' | 'rect' | 'text';

@Component({
  selector: 'app-markup-editor',
  templateUrl: './markup-editor.html',
  styleUrl: './markup-editor.scss',
})
export class MarkupEditorComponent implements OnInit, OnDestroy {
  @ViewChild('canvas', { static: true }) canvasRef!: ElementRef<HTMLCanvasElement>;

  @Input() screenshotDataUrl = '';
  @Output() saved  = new EventEmitter<{ previewBase64: string; canvasBase64: string }>();
  @Output() cancel = new EventEmitter<void>();

  protected activeTool = signal<Tool>('pen');
  protected color      = signal('#ff3b3b');
  protected lineWidth  = signal(3);

  protected readonly tools: { id: Tool; label: string }[] = [
    { id: 'pen',    label: '✏️' },
    { id: 'eraser', label: '⬜' },
    { id: 'arrow',  label: '↗' },
    { id: 'circle', label: '⭕' },
    { id: 'rect',   label: '▭' },
    { id: 'text',   label: 'T' },
  ];

  private ctx!: CanvasRenderingContext2D;
  private drawing = false;
  private startX  = 0;
  private startY  = 0;
  private snapshot: ImageData | null = null;
  private resizeObs!: ResizeObserver;

  ngOnInit(): void {
    const canvas = this.canvasRef.nativeElement;
    this.ctx = canvas.getContext('2d')!;
    this.resizeObs = new ResizeObserver(() => this.fitCanvas());
    this.resizeObs.observe(canvas.parentElement!);
    this.fitCanvas();
  }

  ngOnDestroy(): void {
    this.resizeObs.disconnect();
  }

  protected setTool(t: Tool): void { this.activeTool.set(t); }

  protected onMouseDown(e: MouseEvent): void {
    this.drawing = true;
    const { x, y } = this.pos(e);
    this.startX = x;
    this.startY = y;
    this.snapshot = this.ctx.getImageData(0, 0, this.canvasRef.nativeElement.width, this.canvasRef.nativeElement.height);
    if (this.activeTool() === 'pen' || this.activeTool() === 'eraser') {
      this.ctx.beginPath();
      this.ctx.moveTo(x, y);
    }
    if (this.activeTool() === 'text') {
      const text = prompt('Enter text:');
      if (text) {
        this.ctx.fillStyle = this.color();
        this.ctx.font = `${this.lineWidth() * 6}px Segoe UI, sans-serif`;
        this.ctx.fillText(text, x, y);
      }
      this.drawing = false;
    }
  }

  protected onMouseMove(e: MouseEvent): void {
    if (!this.drawing) return;
    const { x, y } = this.pos(e);
    const tool = this.activeTool();

    if (tool === 'pen') {
      this.ctx.strokeStyle = this.color();
      this.ctx.lineWidth   = this.lineWidth();
      this.ctx.lineTo(x, y);
      this.ctx.stroke();
      return;
    }
    if (tool === 'eraser') {
      this.ctx.clearRect(x - 10, y - 10, 20, 20);
      return;
    }

    // Shape tools: restore snapshot then draw preview
    this.ctx.putImageData(this.snapshot!, 0, 0);
    this.ctx.strokeStyle = this.color();
    this.ctx.lineWidth   = this.lineWidth();
    this.ctx.beginPath();

    if (tool === 'arrow') {
      this.drawArrow(this.startX, this.startY, x, y);
    } else if (tool === 'circle') {
      const rx = Math.abs(x - this.startX) / 2;
      const ry = Math.abs(y - this.startY) / 2;
      const cx = (this.startX + x) / 2;
      const cy = (this.startY + y) / 2;
      this.ctx.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2);
      this.ctx.stroke();
    } else if (tool === 'rect') {
      this.ctx.strokeRect(
        this.startX, this.startY,
        x - this.startX, y - this.startY,
      );
    }
  }

  protected onMouseUp(): void { this.drawing = false; }

  protected save(): void {
    const canvas  = this.canvasRef.nativeElement;
    const offscreen = document.createElement('canvas');
    offscreen.width  = canvas.width;
    offscreen.height = canvas.height;
    const octx = offscreen.getContext('2d')!;

    // composite: screenshot bg + markup layer
    const bg = new Image();
    bg.onload = () => {
      octx.drawImage(bg, 0, 0, canvas.width, canvas.height);
      octx.drawImage(canvas, 0, 0);
      const previewBase64 = offscreen.toDataURL('image/jpeg', 0.8).split(',')[1];
      const canvasBase64  = canvas.toDataURL('image/png').split(',')[1];
      this.saved.emit({ previewBase64, canvasBase64 });
    };
    bg.src = this.screenshotDataUrl;
  }

  private fitCanvas(): void {
    const canvas = this.canvasRef.nativeElement;
    const parent = canvas.parentElement!;
    const prev   = this.ctx.getImageData(0, 0, canvas.width, canvas.height);
    canvas.width  = parent.clientWidth;
    canvas.height = parent.clientHeight;
    this.ctx.putImageData(prev, 0, 0);
  }

  private pos(e: MouseEvent): { x: number; y: number } {
    const rect = this.canvasRef.nativeElement.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  }

  private drawArrow(x1: number, y1: number, x2: number, y2: number): void {
    const angle    = Math.atan2(y2 - y1, x2 - x1);
    const headLen  = 14;
    this.ctx.moveTo(x1, y1);
    this.ctx.lineTo(x2, y2);
    this.ctx.lineTo(
      x2 - headLen * Math.cos(angle - Math.PI / 6),
      y2 - headLen * Math.sin(angle - Math.PI / 6),
    );
    this.ctx.moveTo(x2, y2);
    this.ctx.lineTo(
      x2 - headLen * Math.cos(angle + Math.PI / 6),
      y2 - headLen * Math.sin(angle + Math.PI / 6),
    );
    this.ctx.stroke();
  }
}

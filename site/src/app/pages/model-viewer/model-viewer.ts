import {
  AfterViewInit,
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  NgZone,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import type { Annotation, OrgUser, VersionDetail, ViewerToken } from '../../models/api.models';

declare const Autodesk: any;

interface PinPosition {
  x: number;
  y: number;
}

@Component({
  selector: 'app-model-viewer',
  imports: [RouterLink, FormsModule],
  templateUrl: './model-viewer.html',
  styleUrl: './model-viewer.scss',
})
export class ModelViewerComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('viewerContainer') viewerContainer!: ElementRef<HTMLDivElement>;

  private api  = inject(ApiService);
  private auth = inject(AuthService);
  private cdr  = inject(ChangeDetectorRef);
  private zone = inject(NgZone);
  private route  = inject(ActivatedRoute);
  private router = inject(Router);

  protected partNumber = '';
  protected version    = 0;

  protected versionDetail  = signal<VersionDetail | null>(null);
  protected loading        = signal(true);
  protected error          = signal<string | null>(null);
  protected selectedConfig = signal<string>('');

  protected configs = computed(() => {
    const v = this.versionDetail();
    if (!v) return [];
    if (v.configUrns)          return Object.keys(v.configUrns);
    if (v.configViewableGuids) return Object.keys(v.configViewableGuids);
    return ['Default'];
  });

  // Annotations
  protected annotations    = signal<Annotation[]>([]);
  protected pinPositions   = signal<Map<string, PinPosition>>(new Map());
  protected commentMode    = signal(false);
  protected activePopup    = signal<string | null>(null);   // annotationId
  protected draftText      = signal('');
  protected orgUsers       = signal<OrgUser[]>([]);
  protected mentionQuery   = signal('');
  protected mentionDropdown = signal(false);
  protected mentionStart   = 0;

  protected mentionMatches = computed(() => {
    const q = this.mentionQuery().toLowerCase();
    if (!q) return this.orgUsers().slice(0, 6);
    return this.orgUsers()
      .filter(u => u.name.toLowerCase().includes(q) || u.email.toLowerCase().includes(q))
      .slice(0, 6);
  });

  private viewer: any        = null;
  private viewerToken: ViewerToken | null = null;
  private frameReq: number   = 0;
  private pendingWorld: [number, number, number] | null = null;

  async ngOnInit(): Promise<void> {
    this.partNumber = this.route.snapshot.paramMap.get('pn') ?? '';
    this.version    = Number(this.route.snapshot.paramMap.get('v') ?? '0');
    if (!this.partNumber || !this.version) { this.router.navigate(['/']); return; }

    try {
      const [product, token, users] = await Promise.all([
        this.api.getProduct(this.partNumber),
        this.api.getViewerToken(),
        this.api.getOrgUsers(),
      ]);
      this.viewerToken = token;
      this.orgUsers.set(users);
      const v = product.versions.find(x => x.version === this.version) ?? null;
      this.versionDetail.set(v);
      if (v && this.configs().length > 0) {
        this.selectedConfig.set(this.configs()[0]);
      }
      await this.loadAnnotations();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Failed to load model');
      this.loading.set(false);
    }
  }

  ngAfterViewInit(): void {
    if (this.versionDetail() && this.viewerToken && !this.error()) {
      this.initViewer();
    }
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.frameReq);
    if (this.viewer) {
      this.viewer.finish();
      this.viewer = null;
    }
  }

  protected onConfigChange(e: Event): void {
    const config = (e.target as HTMLSelectElement).value;
    this.selectedConfig.set(config);
    this.loadUrn(config);
    this.activePopup.set(null);
    this.updatePinPositions();
  }

  protected toggleCommentMode(): void {
    this.commentMode.update(v => !v);
    this.activePopup.set(null);
  }

  protected openPopup(id: string): void {
    this.activePopup.set(this.activePopup() === id ? null : id);
    this.draftText.set('');
    this.mentionDropdown.set(false);
  }

  protected closePopup(): void {
    this.activePopup.set(null);
    this.draftText.set(null as any);
    this.draftText.set('');
    this.mentionDropdown.set(false);
  }

  protected getAnnotation(id: string): Annotation | undefined {
    return this.annotations().find(a => a.annotationId === id);
  }

  protected getPinPosition(id: string): PinPosition | undefined {
    return this.pinPositions().get(id);
  }

  protected visibleAnnotations(): Annotation[] {
    const cfg = this.selectedConfig();
    return this.annotations().filter(a => !a.config || a.config === cfg);
  }

  protected onViewerClick(e: MouseEvent): void {
    if (!this.commentMode() || !this.viewer) return;
    const rect = this.viewerContainer.nativeElement.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    const hit = this.viewer.clientToWorld({ x, y });
    if (!hit?.point) return;
    this.pendingWorld = [hit.point.x, hit.point.y, hit.point.z];
    this.activePopup.set('__new__');
    this.draftText.set('');
  }

  protected onDraftInput(e: Event): void {
    const input = e.target as HTMLTextAreaElement;
    const val   = input.value;
    this.draftText.set(val);

    const cursor = input.selectionStart ?? val.length;
    const atIdx  = val.lastIndexOf('@', cursor - 1);
    if (atIdx >= 0 && cursor - atIdx <= 20) {
      this.mentionStart = atIdx;
      this.mentionQuery.set(val.slice(atIdx + 1, cursor));
      this.mentionDropdown.set(true);
    } else {
      this.mentionDropdown.set(false);
    }
  }

  protected insertMention(user: OrgUser, textarea: HTMLTextAreaElement): void {
    const val    = this.draftText();
    const before = val.slice(0, this.mentionStart);
    const after  = val.slice(textarea.selectionStart ?? val.length);
    const next   = `${before}@${user.email} ${after}`;
    this.draftText.set(next);
    this.mentionDropdown.set(false);
    setTimeout(() => textarea.focus());
  }

  protected async submitAnnotation(): Promise<void> {
    const text = this.draftText().trim();
    if (!text || !this.pendingWorld) return;

    const mentionRegex = /@([\w.+-]+@[\w.-]+)/g;
    const mentions: string[] = [];
    let m: RegExpExecArray | null;
    while ((m = mentionRegex.exec(text)) !== null) mentions.push(m[1]);

    try {
      const created = await this.api.createAnnotation(this.partNumber, this.version, {
        config:         this.selectedConfig(),
        worldX:         this.pendingWorld[0],
        worldY:         this.pendingWorld[1],
        worldZ:         this.pendingWorld[2],
        text,
        mentionedEmails: mentions,
      });
      this.annotations.update(list => [...list, created]);
      this.pendingWorld = null;
      this.activePopup.set(null);
      this.commentMode.set(false);
      this.draftText.set('');
      this.updatePinPositions();
    } catch (e) {
      console.error('Failed to save annotation', e);
    }
  }

  protected async deleteAnnotation(id: string): Promise<void> {
    try {
      await this.api.deleteAnnotation(this.partNumber, this.version, id);
      this.annotations.update(list => list.filter(a => a.annotationId !== id));
      if (this.activePopup() === id) this.activePopup.set(null);
      this.updatePinPositions();
    } catch (e) {
      console.error('Failed to delete annotation', e);
    }
  }

  protected currentUserEmail(): string {
    return this.auth.getCurrentUser()?.email ?? '';
  }

  // ── Private ───────────────────────────────────────────────────────────────

  private async loadAnnotations(): Promise<void> {
    try {
      const list = await this.api.getAnnotations(this.partNumber, this.version);
      this.annotations.set(list);
    } catch { /* non-fatal */ }
  }

  private initViewer(): void {
    const options = {
      env: 'AutodeskProduction2',
      api: 'streamingV2',
      accessToken: this.viewerToken!.access_token,
    };
    Autodesk.Viewing.Initializer(options, () => {
      this.viewer = new Autodesk.Viewing.GuiViewer3D(
        this.viewerContainer.nativeElement,
        { extensions: ['Autodesk.DocumentBrowser'] },
      );
      this.viewer.start();
      this.loadUrn(this.selectedConfig());
      this.loading.set(false);
      this.startPinLoop();
    });
  }

  private loadUrn(config: string): void {
    const v = this.versionDetail();
    if (!v || !this.viewer) return;

    let urn = v.apsUrn ?? '';
    if (v.configUrns?.[config]) urn = v.configUrns[config];
    if (!urn) return;

    Autodesk.Viewing.Document.load(
      `urn:${urn}`,
      (doc: any) => {
        const viewable = doc.getRoot().getDefaultGeometry();
        if (!viewable) return;
        this.viewer.loadDocumentNode(doc, viewable).then(() => {
          this.applySuppressedComponents(config);
        });
      },
      (code: number, msg: string) => {
        this.error.set(`Failed to load model: ${msg} (${code})`);
      },
    );
  }

  private applySuppressedComponents(config: string): void {
    const v = this.versionDetail();
    if (!v?.configSuppressedComponents || !this.viewer) return;

    const allConfigs  = this.configs();
    const suppressed  = v.configSuppressedComponents[config] ?? [];
    const suppSet     = new Set(suppressed);
    const allSuppressed = new Set<string>();
    for (const cfg of allConfigs) {
      for (const p of v.configSuppressedComponents[cfg] ?? []) allSuppressed.add(p);
    }

    this.viewer.model?.getObjectTree((tree: any) => {
      const toHide: number[] = [];
      const toShow: number[] = [];
      tree.enumNodeChildren(tree.getRootId(), (dbId: number) => {
        const name = tree.getNodeName(dbId) ?? '';
        if (suppSet.has(name))          toHide.push(dbId);
        else if (allSuppressed.has(name)) toShow.push(dbId);
      }, true);
      if (toHide.length) this.viewer.hide(toHide);
      if (toShow.length) this.viewer.show(toShow);
    });
  }

  private startPinLoop(): void {
    const loop = () => {
      this.updatePinPositions();
      this.frameReq = requestAnimationFrame(loop);
    };
    this.frameReq = requestAnimationFrame(loop);
  }

  private updatePinPositions(): void {
    if (!this.viewer) return;
    const anns = this.visibleAnnotations();
    const map  = new Map<string, PinPosition>();
    for (const a of anns) {
      const screen = this.viewer.worldToClient({ x: a.worldX, y: a.worldY, z: a.worldZ });
      if (screen) map.set(a.annotationId, { x: screen.x, y: screen.y });
    }
    this.zone.run(() => {
      this.pinPositions.set(map);
      this.cdr.markForCheck();
    });
  }
}

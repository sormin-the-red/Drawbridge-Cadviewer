import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService } from '../../services/api.service';
import type { VersionDetail, ViewerToken } from '../../models/api.models';

declare const Autodesk: any;

@Component({
  selector: 'app-model-viewer',
  imports: [RouterLink],
  templateUrl: './model-viewer.html',
  styleUrl: './model-viewer.scss',
})
export class ModelViewerComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('viewerContainer') viewerContainer!: ElementRef<HTMLDivElement>;

  private api    = inject(ApiService);
  private route  = inject(ActivatedRoute);
  private router = inject(Router);

  protected partNumber = '';
  protected version    = 0;

  protected versionDetail = signal<VersionDetail | null>(null);
  protected loading       = signal(true);
  protected error         = signal<string | null>(null);
  protected selectedConfig = signal<string>('');

  protected configs = computed(() => {
    const v = this.versionDetail();
    if (!v) return [];
    if (v.configUrns)    return Object.keys(v.configUrns);
    if (v.configViewableGuids) return Object.keys(v.configViewableGuids);
    return ['Default'];
  });

  private viewer: any = null;
  private viewerToken: ViewerToken | null = null;

  async ngOnInit(): Promise<void> {
    this.partNumber = this.route.snapshot.paramMap.get('pn') ?? '';
    this.version    = Number(this.route.snapshot.paramMap.get('v') ?? '0');
    if (!this.partNumber || !this.version) { this.router.navigate(['/']); return; }

    try {
      const [product, token] = await Promise.all([
        this.api.getProduct(this.partNumber),
        this.api.getViewerToken(),
      ]);
      this.viewerToken = token;
      const v = product.versions.find(x => x.version === this.version) ?? null;
      this.versionDetail.set(v);
      if (v && this.configs().length > 0) {
        this.selectedConfig.set(this.configs()[0]);
      }
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Failed to load model');
      this.loading.set(false);
    }
  }

  ngAfterViewInit(): void {
    const v = this.versionDetail();
    if (v && this.viewerToken && !this.error()) {
      this.initViewer();
    }
  }

  ngOnDestroy(): void {
    if (this.viewer) {
      this.viewer.finish();
      this.viewer = null;
    }
  }

  protected onConfigChange(e: Event): void {
    const config = (e.target as HTMLSelectElement).value;
    this.selectedConfig.set(config);
    this.loadUrn(config);
  }

  private initViewer(): void {
    const options = {
      env: 'AutodeskProduction2',
      api: 'streamingV2',
      accessToken: this.viewerToken!.access_token,
    };
    Autodesk.Viewing.Initializer(options, () => {
      const config3d = { extensions: ['Autodesk.DocumentBrowser'] };
      this.viewer = new Autodesk.Viewing.GuiViewer3D(
        this.viewerContainer.nativeElement,
        config3d,
      );
      this.viewer.start();
      this.loadUrn(this.selectedConfig());
      this.loading.set(false);
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

    const allConfigs = this.configs();
    const suppressed = v.configSuppressedComponents[config] ?? [];
    const suppSet = new Set(suppressed);

    // collect all component paths suppressed in OTHER configs to restore them
    const allSuppressed = new Set<string>();
    for (const cfg of allConfigs) {
      for (const p of v.configSuppressedComponents[cfg] ?? []) {
        allSuppressed.add(p);
      }
    }

    this.viewer.model?.getObjectTree((tree: any) => {
      const toHide: number[] = [];
      const toShow: number[] = [];
      tree.enumNodeChildren(tree.getRootId(), (dbId: number) => {
        const name = tree.getNodeName(dbId) ?? '';
        if (suppSet.has(name))    toHide.push(dbId);
        else if (allSuppressed.has(name)) toShow.push(dbId);
      }, true);
      if (toHide.length) this.viewer.hide(toHide);
      if (toShow.length) this.viewer.show(toShow);
    });
  }
}

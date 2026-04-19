import { Injectable, inject } from '@angular/core';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';
import type {
  ProductSummary,
  ProductDetail,
  JobStatus,
  ViewerToken,
  Annotation,
  CreateAnnotationRequest,
  MarkupRecord,
  MarkupData,
  CreateMarkupRequest,
  OrgUser,
} from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private auth = inject(AuthService);

  private async get<T>(path: string): Promise<T> {
    const token = await this.auth.getIdToken();
    const res = await fetch(`${environment.apiBaseUrl}${path}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!res.ok) throw new Error(`GET ${path} → ${res.status}`);
    return res.json() as Promise<T>;
  }

  private async post<T>(path: string, body: unknown): Promise<T> {
    const token = await this.auth.getIdToken();
    const res = await fetch(`${environment.apiBaseUrl}${path}`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(body),
    });
    if (!res.ok) throw new Error(`POST ${path} → ${res.status}`);
    return res.json() as Promise<T>;
  }

  private async del(path: string): Promise<void> {
    const token = await this.auth.getIdToken();
    const res = await fetch(`${environment.apiBaseUrl}${path}`, {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!res.ok) throw new Error(`DELETE ${path} → ${res.status}`);
  }

  // ── Products ─────────────────────────────────────────────────────────────

  getProducts(): Promise<ProductSummary[]> {
    return this.get('/products');
  }

  getProduct(partNumber: string): Promise<ProductDetail> {
    return this.get(`/products/${encodeURIComponent(partNumber)}`);
  }

  // ── Jobs ─────────────────────────────────────────────────────────────────

  getJob(jobId: string): Promise<JobStatus> {
    return this.get(`/jobs/${encodeURIComponent(jobId)}`);
  }

  // ── Viewer ───────────────────────────────────────────────────────────────

  getViewerToken(): Promise<ViewerToken> {
    return this.get('/viewer-token');
  }

  // ── Annotations ──────────────────────────────────────────────────────────

  getAnnotations(partNumber: string, version: number): Promise<Annotation[]> {
    return this.get(`/products/${encodeURIComponent(partNumber)}/versions/${version}/annotations`);
  }

  createAnnotation(
    partNumber: string,
    version: number,
    req: CreateAnnotationRequest,
  ): Promise<Annotation> {
    return this.post(
      `/products/${encodeURIComponent(partNumber)}/versions/${version}/annotations`,
      req,
    );
  }

  deleteAnnotation(partNumber: string, version: number, annotationId: string): Promise<void> {
    return this.del(
      `/products/${encodeURIComponent(partNumber)}/versions/${version}/annotations/${encodeURIComponent(annotationId)}`,
    );
  }

  getOrgUsers(): Promise<OrgUser[]> {
    return this.get('/org-users');
  }

  // ── Markups ──────────────────────────────────────────────────────────────

  getMarkups(partNumber: string, version: number): Promise<MarkupRecord[]> {
    return this.get(`/products/${encodeURIComponent(partNumber)}/versions/${version}/markups`);
  }

  createMarkup(
    partNumber: string,
    version: number,
    req: CreateMarkupRequest,
  ): Promise<MarkupRecord> {
    return this.post(
      `/products/${encodeURIComponent(partNumber)}/versions/${version}/markups`,
      req,
    );
  }

  deleteMarkup(partNumber: string, version: number, markupId: string): Promise<void> {
    return this.del(
      `/products/${encodeURIComponent(partNumber)}/versions/${version}/markups/${encodeURIComponent(markupId)}`,
    );
  }

  updateMarkupOrder(
    partNumber: string,
    version: number,
    markupId: string,
    sortOrder: number,
  ): Promise<void> {
    return this.post(
      `/products/${encodeURIComponent(partNumber)}/versions/${version}/markups/${encodeURIComponent(markupId)}/order`,
      { sortOrder },
    );
  }

  getMarkupData(dataUrl: string): Promise<MarkupData> {
    return fetch(dataUrl).then(r => {
      if (!r.ok) throw new Error(`Markup data fetch failed: ${r.status}`);
      return r.json() as Promise<MarkupData>;
    });
  }
}

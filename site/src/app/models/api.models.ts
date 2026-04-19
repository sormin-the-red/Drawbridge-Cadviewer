export interface ProductSummary {
  partNumber: string;
  name: string;
  latestVersion: number;
  latestStatus: string;
  thumbnailUrl: string | null;
  description: string;
}

export interface VersionDetail {
  version: number;
  status: string;
  submittedAt: string;
  submittedBy: string;
  description: string;
  ownerName: string;
  ownerEmail: string;
  apsUrn: string | null;
  configUrns: Record<string, string> | null;
  configViewableGuids: Record<string, string> | null;
  configSuppressedComponents: Record<string, string[]> | null;
  fbxUrls: string[];
  stlUrls: string[];
  skpUrls: string[];
  thumbnailUrl: string | null;
  errorMessage: string | null;
}

export interface ProductDetail {
  partNumber: string;
  name: string;
  description: string;
  versions: VersionDetail[];
}

export interface JobStatus {
  jobId: string;
  status: string;
  progress: string | null;
  errorMessage: string | null;
  createdAt: string;
}

export interface ViewerToken {
  access_token: string;
  token_type: string;
  expires_in: number;
}

export interface Annotation {
  annotationId: string;
  partNumber: string;
  version: number;
  config: string;
  worldX: number;
  worldY: number;
  worldZ: number;
  text: string;
  authorEmail: string;
  authorName: string;
  createdAt: string;
  mentionedEmails: string[];
}

export interface CreateAnnotationRequest {
  config: string;
  worldX: number;
  worldY: number;
  worldZ: number;
  text: string;
  mentionedEmails: string[];
}

export interface MarkupRecord {
  markupId: string;
  partNumber: string;
  version: number;
  config: string;
  label: string;
  sortOrder: number;
  previewUrl: string;
  dataUrl: string;
  createdAt: string;
  createdBy: string;
}

export interface MarkupData {
  viewerState: unknown;
  canvasDataUrl: string;
}

export interface CreateMarkupRequest {
  config: string;
  label: string;
  sortOrder: number;
  previewBase64: string;
  canvasBase64: string;
  viewerState: unknown;
}

export interface OrgUser {
  email: string;
  name: string;
}

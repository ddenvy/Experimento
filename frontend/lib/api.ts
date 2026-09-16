// Минимальный типизированный API-клиент бэкенда Experimento.
// В продакшене типы должны генерироваться из OpenAPI-спецификации.

// Access-токен живёт ТОЛЬКО в памяти модуля (не в localStorage — это XSS-поверхность).
// Refresh-токен хранится в httpOnly-cookie и не доступен из JS.
let accessToken: string | null = null;

export function getAccessToken(): string | null {
  return accessToken;
}

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

// Серверный SSR (в Docker) обращается по внутреннему имени сервиса;
// браузер — по публичному URL.
export function getBaseUrl(): string {
  if (typeof window === "undefined") {
    return process.env.API_INTERNAL_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5126/api";
  }
  return process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5126/api";
}

const BASE_URL = getBaseUrl();

// Базовый адрес SignalR-хаба (используется только в браузере).
export function getHubUrl(): string {
  return new URL("/hubs/jobs", BASE_URL).toString();
}

// ---- Доменные типы (зеркала серверных DTO, camelCase JSON) ----

export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  role: string;
}

export interface AuthResponse {
  user: UserDto;
  accessToken: string;
}

export interface ProjectDto {
  id: string;
  name: string;
  description: string | null;
  createdAtUtc: string;
}

export interface FormulationDto {
  id: string;
  projectId: string;
  name: string;
  targetPurpose: string;
  currentVersionNumber: number;
}

export interface ComponentInput {
  chemicalName: string;
  casNumber?: string | null;
  formula?: string | null;
  molarMass: number;
  proportion: number;
  role?: string;
  pubChemCid?: number | null;
}

export interface ConditionsInput {
  temperatureCelsius: number;
  phTarget?: number;
  solvent?: string;
}

export interface ComponentDto {
  id: string;
  chemicalName: string;
  casNumber: string | null;
  formula: string | null;
  molarMass: number;
  proportion: number;
  role: string | null;
  pubChemCid: number | null;
}

// Вещество из каталога PubChem.
export interface ChemicalDto {
  pubChemCid: number;
  name: string;
  casNumber: string | null;
  formula: string | null;
  molarMass: number;
  smiles: string | null;
}

// Регуляторные органы и статусы (mirror backend enum).
export type RegulationAuthority =
  | "ReachSvhc"
  | "EpaPfas"
  | "CaliforniaProp65"
  | "Voc";
export type RegulationStatus = "Compliant" | "Restricted" | "Banned";

export interface ChemicalRegulationDto {
  authority: RegulationAuthority;
  status: RegulationStatus;
  reason: string;
  sourceUrl: string | null;
}

export interface ChemicalRegulationSummaryDto {
  pubChemCid: number;
  highestStatus: RegulationStatus;
  regulations: ChemicalRegulationDto[];
}

// Кандидат автоподсказки: cid заполнен для каталога/CAS/формулы, null — для имени.
export interface ChemicalSuggestion {
  pubChemCid: number | null;
  name: string;
  formula: string | null;
  // local | name | formula | cas
  matchType: "local" | "name" | "formula" | "cas";
}

export interface ConditionsDto {
  temperatureCelsius: number;
  pressureKPa: number | null;
  phTarget: number | null;
  solvent: string | null;
  deliveryTarget: string | null;
}

export interface FormulationVersionDto {
  id: string;
  formulationId: string;
  versionNumber: number;
  status: string;
  notes: string | null;
  createdAtUtc: string;
  components: ComponentDto[];
  conditions: ConditionsDto;
}

export interface JobDto {
  id: string;
  versionId: string;
  status: string;
  progress: number;
  stage?: string | null;
  createdAtUtc: string;
}

export interface RationaleSourceDto {
  title: string;
  reference: string;
  type: string;
  similarity: number;
}

export interface RationaleItemDto {
  id: string;
  category: string;
  claim: string;
  explanation: string;
  confidence: number;
  sources: RationaleSourceDto[];
}

export interface ReviewDto {
  id: string;
  decision: string;
  comment: string | null;
  createdAtUtc: string;
}

export interface OutcomeDto {
  id: string;
  actualSuccess: boolean;
  actualMetricsJson: string;
  notes: string | null;
  recordedAtUtc: string;
}

export interface PredictionResultDto {
  id: string;
  jobId: string;
  modelRegistrationId: string;
  modelDisplayName: string;
  successProbability: number;
  toxicityScore: number;
  stabilityScore: number;
  sideRiskLevel: string;
  summary: string;
  rationaleItems: RationaleItemDto[];
  reviews: ReviewDto[];
  outcome: OutcomeDto | null;
}

export interface SimulationCandidateDto {
  id: string;
  rank: number;
  successProbability: number;
  score: number;
  parametersJson: string;
}

export interface SimulationResultDto {
  id: string;
  jobId: string;
  iterationsExecuted: number;
  summary: string;
  bestCandidate: SimulationCandidateDto | null;
  topCandidates: SimulationCandidateDto[];
}

export interface KnowledgeDocumentDto {
  id: string;
  title: string;
  sourceType: string;
  reference: string;
  status: string;
  uploadedAtUtc: string;
}

export interface SearchResultDto {
  chunkId: string;
  documentTitle: string;
  reference: string;
  sourceType: string;
  content: string;
  similarity: number;
}

export interface AuditEntryDto {
  id: number;
  timestampUtc: string;
  actorUserId: string | null;
  action: string;
  entityType: string;
  entityId: string | null;
}

export interface AuditIntegrityDto {
  isIntact: boolean;
  firstBrokenId: number | null;
}

// Сводки истории прогонов по версии.
export interface PredictionRunSummaryDto {
  jobId: string;
  resultId: string | null;
  status: string;
  modelDisplayName: string;
  successProbability: number;
  toxicityScore: number;
  stabilityScore: number;
  sideRiskLevel: string;
  hasOutcome: boolean;
  createdAtUtc: string;
}

export interface SimulationRunSummaryDto {
  jobId: string;
  resultId: string | null;
  status: string;
  iterationsExecuted: number;
  bestSuccessProbability: number | null;
  bestScore: number | null;
  createdAtUtc: string;
}

export interface CalibrationStatsDto {
  total: number;
  withOutcome: number;
  meanError: number;
  meanBias: number;
}

export interface ModelRegistrationDto {
  id: string;
  name: string;
  version: string;
  description: string;
  contextOfUse: string;
  registeredAtUtc: string;
}

export interface ModelScorecardDto {
  modelId: string;
  displayName: string;
  version: string;
  contextOfUse: string;
  total: number;
  withOutcome: number;
  meanError: number;
  meanBias: number;
}

export interface ComponentDiff {
  chemicalName: string;
  change: "Added" | "Removed" | "Modified";
  proportionA: number | null;
  proportionB: number | null;
  molarMassA: number | null;
  molarMassB: number | null;
}

export interface VersionComparisonDto {
  versionA: FormulationVersionDto;
  versionB: FormulationVersionDto;
  componentDiffs: ComponentDiff[];
}

export interface RecentRunDto {
  kind: "prediction" | "simulation";
  jobId: string;
  status: string;
  projectId: string;
  projectName: string;
  formulationId: string;
  formulationName: string;
  versionNumber: number;
  metric: number | null;
  createdAtUtc: string;
}

export interface MeDto {
  id: string;
  email: string;
}

export interface SubmitSimulationInput {
  iterations: number;
  varyConcentrations: boolean;
  varyTemperature: boolean;
  varyPh: boolean;
  seed?: number;
  targetMetric: string;
}

export class ApiError extends Error {
  constructor(message: string, public status: number) {
    super(message);
  }
}

// ---- Единоразовое обновление access-токена (single-flight: все параллельные
// 401 ждут один запрос /auth/refresh, а не шлют пачку refresh) ----
let refreshInFlight: Promise<boolean> | null = null;

export async function refreshAccessToken(): Promise<boolean> {
  if (refreshInFlight) return refreshInFlight;

  refreshInFlight = (async () => {
    try {
      const res = await fetch(`${BASE_URL}/auth/refresh`, {
        method: "POST",
        credentials: "include",
      });
      if (!res.ok) {
        accessToken = null;
        return false;
      }
      const body = (await res.json()) as { accessToken?: string };
      if (!body.accessToken) {
        accessToken = null;
        return false;
      }
      accessToken = body.accessToken;
      return true;
    } catch {
      accessToken = null;
      return false;
    } finally {
      // Сбрасываем после того, как все ожидающие получили результат.
      refreshInFlight = null;
    }
  })();

  return refreshInFlight;
}

function redirectToLogin(): void {
  // Жёсткая навигация с полной перезагрузкой страницы: модуль вне React, useRouter
  // недоступен, а при истёкшей сессии нужна именно полная перезагрузка на /login.
  if (typeof window !== "undefined" && !window.location.pathname.startsWith("/login")) {
    // eslint-disable-next-line @next/next/no-location-assign-relative-destination -- намеренный hard redirect
    window.location.href = "/login";
  }
}

async function parseErrorMessage(res: Response): Promise<string> {
  try {
    const data: unknown = await res.json();
    if (data && typeof data === "object" && "error" in data) {
      const error = (data as { error: unknown }).error;
      if (typeof error === "string") return error;
    }
    if (data && typeof data === "object" && "details" in data) {
      const details = (data as { details?: Array<{ errorMessage?: string }> }).details;
      if (Array.isArray(details)) return details.map((d) => d.errorMessage).filter(Boolean).join(", ");
    }
  } catch {
    // тело ответа не JSON
  }
  return "";
}

async function request<T>(
  path: string,
  options: RequestInit = {},
  isRetry = false,
  // Не-JSON ответы (например, Markdown-отчёт) парсятся переданным парсером.
  parse: (res: Response) => Promise<T> = async (res) => (await res.json()) as T
): Promise<T> {
  // Для FormData заголовок Content-Type задаёт сам браузер вместе с boundary multipart.
  const isFormData = typeof FormData !== "undefined" && options.body instanceof FormData;
  const res = await fetch(`${BASE_URL}${path}`, {
    ...options,
    credentials: "include",
    headers: {
      ...(isFormData ? {} : { "Content-Type": "application/json" }),
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...options.headers,
    },
  });

  if (res.status === 401 && !isRetry && !path.startsWith("/auth/")) {
    // Access-токен истёк — один раз пробуем обновить его через cookie и повторяем запрос.
    if (await refreshAccessToken()) {
      return request<T>(path, options, true, parse);
    }
    redirectToLogin();
    throw new ApiError("Session expired", 401);
  }

  if (!res.ok) {
    const msg = await parseErrorMessage(res);
    throw new ApiError(msg || `Request failed (${res.status})`, res.status);
  }
  if (res.status === 204) return undefined as T;
  return parse(res);
}

export const api = {
  // Auth
  register: (data: { email: string; password: string; displayName: string }) =>
    request<AuthResponse>("/auth/register", { method: "POST", body: JSON.stringify(data) })
      .then((res) => {
        setAccessToken(res.accessToken);
        return res;
      }),
  login: (data: { email: string; password: string }) =>
    request<AuthResponse>("/auth/login", { method: "POST", body: JSON.stringify(data) })
      .then((res) => {
        setAccessToken(res.accessToken);
        return res;
      }),
  logout: async (): Promise<void> => {
    try {
      await request("/auth/logout", { method: "POST" });
    } finally {
      setAccessToken(null);
    }
  },

  // Chemicals (PubChem catalog)
  suggestChemicals: (query: string, limit = 8) =>
    request<ChemicalSuggestion[]>(`/chemicals/suggest?query=${encodeURIComponent(query)}&limit=${limit}`),
  resolveChemical: (name: string) =>
    request<ChemicalDto>(`/chemicals/resolve?name=${encodeURIComponent(name)}`),
  resolveChemicalByCid: (cid: number) =>
    request<ChemicalDto>(`/chemicals/resolve-cid/${cid}`),
  getChemicalRegulations: (cid: number) =>
    request<ChemicalRegulationSummaryDto>(`/chemicals/regulations/${cid}`),
  getChemicalRegulationsBatch: (cids: number[]) =>
    request<ChemicalRegulationSummaryDto[]>("/chemicals/regulations/batch", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cids),
    }),

  // Formulations
  listProjects: () => request<ProjectDto[]>("/projects"),
  getProject: (projectId: string) => request<ProjectDto>(`/projects/${projectId}`),
  createProject: (data: { name: string; description?: string }) =>
    request<ProjectDto>("/projects", { method: "POST", body: JSON.stringify(data) }),

  listFormulations: (projectId: string) =>
    request<FormulationDto[]>(`/formulations/projects/${projectId}`),
  createFormulation: (data: { projectId: string; name: string; targetPurpose: string }) =>
    request<FormulationDto>("/formulations", { method: "POST", body: JSON.stringify(data) }),
  listVersions: (formulationId: string) =>
    request<FormulationVersionDto[]>(`/formulations/${formulationId}/versions`),
  createVersion: (formulationId: string, data: {
    components: ComponentInput[];
    conditions: ConditionsInput;
    notes?: string;
  }) =>
    request<FormulationVersionDto>(`/formulations/${formulationId}/versions`, {
      method: "POST",
      body: JSON.stringify(data),
    }),

  // Predictions
  submitPrediction: (versionId: string) =>
    request<JobDto>(`/predictions/formulation-versions/${versionId}/predictions`, { method: "POST" }),
  listPredictionRuns: (versionId: string) =>
    request<PredictionRunSummaryDto[]>(`/predictions/formulation-versions/${versionId}/predictions`),
  getPredictionJob: (jobId: string) =>
    request<JobDto>(`/predictions/prediction-jobs/${jobId}`),
  getPredictionResult: (jobId: string) =>
    request<PredictionResultDto>(`/predictions/prediction-jobs/${jobId}/result`),
  submitReview: (
    resultId: string,
    data: { decision: string; comment?: string }
  ) =>
    request<ReviewDto>(`/predictions/prediction-results/${resultId}/review`, {
      method: "POST",
      body: JSON.stringify(data),
    }),
  recordOutcome: (
    resultId: string,
    data: { actualSuccess: boolean; actualMetricsJson: string; notes?: string }
  ) =>
    request<OutcomeDto>(`/predictions/prediction-results/${resultId}/outcome`, {
      method: "POST",
      body: JSON.stringify(data),
    }),
  getCalibration: () => request<CalibrationStatsDto>("/predictions/calibration"),

  // Simulations
  submitSimulation: (versionId: string, data: SubmitSimulationInput) =>
    request<JobDto>(`/simulations/formulation-versions/${versionId}/simulations`, {
      method: "POST",
      body: JSON.stringify(data),
    }),
  getSimulationJob: (jobId: string) =>
    request<JobDto>(`/simulations/simulation-jobs/${jobId}`),
  getSimulationResult: (jobId: string) =>
    request<SimulationResultDto>(`/simulations/simulation-jobs/${jobId}/result`),
  listSimulationRuns: (versionId: string) =>
    request<SimulationRunSummaryDto[]>(`/simulations/formulation-versions/${versionId}/simulations`),

  // Models
  listModels: () => request<ModelRegistrationDto[]>("/models"),
  getModelScorecard: () => request<ModelScorecardDto[]>("/models/scorecard"),

  // Version comparison
  compareVersions: (formulationId: string, a: string, b: string) =>
    request<VersionComparisonDto>(
      `/formulations/${formulationId}/compare?a=${encodeURIComponent(a)}&b=${encodeURIComponent(b)}`
    ),

  // Reports (Markdown text — не JSON)
  getVersionReport: (versionId: string) =>
    request<string>(
      `/reports/formulation-versions/${versionId}/report`,
      {},
      false,
      (res) => res.text()
    ),

  // Knowledge
  listDocuments: (projectId?: string) =>
    request<KnowledgeDocumentDto[]>("/knowledge/documents" + (projectId ? `?projectId=${encodeURIComponent(projectId)}` : "")),
  uploadDocument: (input: {
    title: string;
    sourceType: string;
    reference?: string;
    content: string;
    projectId?: string;
  }) =>
    request<KnowledgeDocumentDto>("/knowledge/documents", {
      method: "POST",
      body: JSON.stringify({
        title: input.title,
        sourceType: input.sourceType,
        reference: input.reference ?? "",
        content: input.content,
        projectId: input.projectId ?? null,
      }),
    }),
  uploadDocumentFile: (
    file: File,
    meta?: { sourceType?: string; reference?: string; projectId?: string }
  ) => {
    const form = new FormData();
    form.append("file", file);
    if (meta?.sourceType) form.append("sourceType", meta.sourceType);
    if (meta?.reference) form.append("reference", meta.reference);
    if (meta?.projectId) form.append("projectId", meta.projectId);
    return request<KnowledgeDocumentDto>("/knowledge/documents/upload", {
      method: "POST",
      body: form,
    });
  },
  searchKnowledge: (query: string, projectId?: string) =>
    request<SearchResultDto[]>("/knowledge/search", {
      method: "POST",
      body: JSON.stringify({ query, projectId }),
    }),

  // Activity / profile
  getRecentRuns: () => request<RecentRunDto[]>("/activity/recent-runs"),
  getMe: () => request<MeDto>("/auth/me"),

  // Audit
  getAuditTrail: (entityType?: string, entityId?: string) =>
    request<AuditEntryDto[]>(
      "/audit" + (entityType ? `?entityType=${encodeURIComponent(entityType)}&entityId=${encodeURIComponent(entityId ?? "")}` : "")
    ),
  verifyAudit: () => request<AuditIntegrityDto>("/audit/verify"),
  // Пакет аудита — Markdown-текст, не JSON.
  exportAuditPackage: () =>
    request<string>("/audit/export", {}, false, (res) => res.text()),
};

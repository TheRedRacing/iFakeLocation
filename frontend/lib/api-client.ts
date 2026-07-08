import type {
  DevicesResponse,
  HomeCountryResponse,
  ProblemDetails,
  RoutePointDto,
  RouteStatusResponse,
  VersionResponse,
} from "@/lib/types";

// The static export is served by the same .NET process at the same origin, so relative paths
// resolve correctly without any base-URL configuration.
const API_BASE = "/api";

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ProblemDetails;

  constructor(status: number, problem?: ProblemDetails) {
    super(problem?.detail ?? problem?.title ?? `Request failed with status ${status}`);
    this.status = status;
    this.problem = problem;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, init);

  if (!response.ok) {
    let problem: ProblemDetails | undefined;
    try {
      problem = (await response.json()) as ProblemDetails;
    } catch {
      // Body wasn't JSON (or was empty) -- surface the bare status instead.
    }
    throw new ApiError(response.status, problem);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

function jsonBody(body: unknown): RequestInit {
  return {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  };
}

export const apiClient = {
  getVersion: () => request<VersionResponse>("/version"),
  getHomeCountry: () => request<HomeCountryResponse>("/home-country"),
  exit: () => request<void>("/exit", { method: "POST" }),

  getDevices: () => request<DevicesResponse>("/devices"),

  // Both of these gate on device readiness (developer-mode toggle + image mount) internally on
  // the backend before acting -- there is no separate "check dependencies" step or granular
  // download-progress polling. pymobiledevice3's mount step resolves/downloads/mounts the correct
  // image as one call with no byte-level progress to report; the frontend just shows an
  // indeterminate "Preparing device..." state while these calls are in flight (see
  // components/dialogs/download-progress-dialog.tsx).
  setLocation: (udid: string, point: RoutePointDto) =>
    request<void>(`/devices/${encodeURIComponent(udid)}/location`, jsonBody(point)),
  stopLocation: (udid: string) =>
    request<void>(`/devices/${encodeURIComponent(udid)}/location`, { method: "DELETE" }),

  startRoute: (udid: string, points: RoutePointDto[], speedKmh: number, loop: boolean) =>
    request<RouteStatusResponse>(`/devices/${encodeURIComponent(udid)}/route/start`, jsonBody({ points, speedKmh, loop })),
  pauseRoute: (udid: string) =>
    request<RouteStatusResponse>(`/devices/${encodeURIComponent(udid)}/route/pause`, { method: "POST" }),
  resumeRoute: (udid: string) =>
    request<RouteStatusResponse>(`/devices/${encodeURIComponent(udid)}/route/resume`, { method: "POST" }),
  stopRoute: (udid: string) =>
    request<void>(`/devices/${encodeURIComponent(udid)}/route/stop`, { method: "POST" }),
  getRouteStatus: (udid: string) =>
    request<RouteStatusResponse>(`/devices/${encodeURIComponent(udid)}/route/status`),
};

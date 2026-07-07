// Hand-kept in sync with the backend's Contracts/*.cs DTOs. An OpenAPI-generated client would
// keep this in lockstep automatically -- out of scope for now, noted as a future improvement.

export interface DeviceDto {
  name: string;
  displayName: string;
  udid: string;
  isNetwork: boolean;
}

export interface DevicesResponse {
  devices: DeviceDto[];
}

export interface DependencyCheckResponse {
  hasDependencies: boolean;
  iosVersion: string;
}

export interface DownloadProgressResponse {
  done: boolean;
  fileName: string | null;
  progressPercent: number | null;
}

export interface VersionResponse {
  version: string;
}

export interface HomeCountryResponse {
  displayName: string;
}

export interface RoutePointDto {
  lat: number;
  lng: number;
}

export type RouteSimulationState = "running" | "paused" | "completed";

export interface RouteStatusResponse {
  state: RouteSimulationState;
  currentPosition: RoutePointDto;
  progressPercent: number;
  elapsedSeconds: number;
  totalSeconds: number;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
}

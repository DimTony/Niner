import axios from "axios";

const api = axios.create({
  baseURL: process.env.NEXT_PUBLIC_API_URL,
  headers: { "Content-Type": "application/json" },
});

export type JobStatus =
  | "pending"
  | "blocked"
  | "processing"
  | "completed"
  | "failed"
  | "cancelled";

export type JobPriority = "high" | "medium" | "low";
export type JobType = "sendEmail" | "webhookDelivery" | "logProcessor";
export type JobRecurrence = "every1Minute" | "every5Minutes" | "every1Hour";

export interface Job {
  id: string;
  type: JobType;
  payload: string;
  priority: JobPriority;
  status: JobStatus;
  retryCount: number;
  maxRetries: number;
  scheduledAt: string;
  recurrence: JobRecurrence | null;
  createdAt: string;
  updatedAt: string;
  lastError: string | null;
  dependsOn: string[] | null;
}

export interface DlqEntry {
  id: string;
  jobId: string;
  errorDetails: string;
  failureCount: number;
  createdAt: string;
  resolvedAt: string | null;
  resolved: boolean;
  job: Job | null;
}

export interface DashboardData {
  statusCounts: Record<JobStatus, number>;
  generatedAt: string;
}

export interface CreateJobPayload {
  type: JobType;
  payload: string;
  priority: JobPriority;
  scheduledAt?: string;
  recurrence?: JobRecurrence;
  dependsOn?: string[];
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

// Jobs
export const getJobs = (status?: JobStatus, page = 1, pageSize = 20) =>
  api
    .get<PagedResponse<Job>>("/api/jobs", {
      params: { status, page, pageSize },
    })
    .then((r) => r.data);

export const getJob = (id: string) =>
  api.get<Job>(`/api/jobs/${id}`).then((r) => r.data);

export const createJob = (data: CreateJobPayload) =>
  api.post<Job>("/api/jobs", data).then((r) => r.data);

export const cancelJob = (id: string) =>
  api.post<Job>(`/api/jobs/${id}/cancel`).then((r) => r.data);

export const getDashboard = () =>
  api.get<DashboardData>("/api/jobs/dashboard").then((r) => r.data);

// DLQ
export const getDlq = () => api.get<DlqEntry[]>("/api/dlq").then((r) => r.data);

export const retryDlqJob = (jobId: string) =>
  api.post<Job>(`/api/dlq/${jobId}/retry`).then((r) => r.data);

export default api;

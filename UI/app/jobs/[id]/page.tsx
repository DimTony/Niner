/* eslint-disable @typescript-eslint/no-explicit-any */
"use client";

import { useQuery } from "@tanstack/react-query";
import { getJob } from "@/lib/api";
import { StatusBadge } from "@/components/StatusBadge";
import { PriorityBadge } from "@/components/PriorityBadge";
import { useParams } from "next/navigation";
import Link from "next/link";
import api from "@/lib/api";

export default function JobDetailPage() {
  const { id } = useParams<{ id: string }>();

  const { data: job, isLoading } = useQuery({
    queryKey: ["job", id],
    queryFn: () => getJob(id),
  });

  const { data: logs } = useQuery({
    queryKey: ["job-logs", id],
    queryFn: () => api.get(`/api/jobs/${id}/logs`).then((r) => r.data),
    enabled: !!id,
  });

  if (isLoading) return <main className="p-8 text-gray-400">Loading...</main>;
  if (!job) return <main className="p-8 text-gray-400">Job not found.</main>;

  return (
    <main className="p-8 max-w-3xl mx-auto">
      <Link
        href="/jobs"
        className="text-sm text-gray-500 hover:text-gray-900 mb-4 inline-block"
      >
        ← Back to Jobs
      </Link>

      <div className="bg-white border border-gray-200 rounded-lg p-6 shadow-sm mb-6">
        <div className="flex items-center gap-3 mb-4">
          <h1 className="text-lg font-semibold text-gray-900">{job.type}</h1>
          <StatusBadge status={job.status} />
          <PriorityBadge priority={job.priority} />
        </div>

        <dl className="grid grid-cols-2 gap-3 text-sm">
          {[
            ["ID", job.id],
            ["Retries", `${job.retryCount} / ${job.maxRetries}`],
            ["Scheduled", new Date(job.scheduledAt).toLocaleString()],
            ["Created", new Date(job.createdAt).toLocaleString()],
            ["Updated", new Date(job.updatedAt).toLocaleString()],
            ["Recurrence", job.recurrence ?? "None"],
          ].map(([label, value]) => (
            <div key={label}>
              <dt className="text-gray-500">{label}</dt>
              <dd className="font-mono text-xs text-gray-800 break-all">
                {value}
              </dd>
            </div>
          ))}
        </dl>

        {job.lastError && (
          <div className="mt-4 bg-red-50 border border-red-100 rounded px-3 py-2">
            <p className="text-xs font-medium text-red-700 mb-1">Last Error</p>
            <p className="text-xs font-mono text-red-600">{job.lastError}</p>
          </div>
        )}

        <div className="mt-4">
          <p className="text-xs text-gray-500 mb-1">Payload</p>
          <pre
            className="text-xs bg-gray-50 border border-gray-200 rounded
                          px-3 py-2 overflow-x-auto"
          >
            {JSON.stringify(JSON.parse(job.payload), null, 2)}
          </pre>
        </div>
      </div>

      {/* Event log */}
      <h2 className="text-sm font-medium text-gray-700 mb-3">Event Log</h2>
      <div className="space-y-2">
        {(logs ?? []).map((log: any) => (
          <div
            key={log.id}
            className="bg-white border border-gray-100 rounded px-4 py-3 text-xs"
          >
            <div className="flex items-center gap-3 mb-1">
              <span className="font-medium text-gray-700">{log.event}</span>
              <span className="text-gray-400">
                {new Date(log.createdAt).toLocaleString()}
              </span>
            </div>
            <p className="text-gray-600">{log.message}</p>
          </div>
        ))}
        {(!logs || logs.length === 0) && (
          <p className="text-xs text-gray-400">No logs yet.</p>
        )}
      </div>
    </main>
  );
}

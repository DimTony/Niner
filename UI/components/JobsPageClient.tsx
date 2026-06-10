"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getJobs, cancelJob, JobStatus } from "@/lib/api";
import { StatusBadge } from "@/components/StatusBadge";
import { PriorityBadge } from "@/components/PriorityBadge";
import { useSearchParams, useRouter } from "next/navigation";
import Link from "next/link";

const STATUSES: (JobStatus | "")[] = [
  "",
  "pending",
  "blocked",
  "processing",
  "completed",
  "failed",
  "cancelled",
];

export default function JobsPageClient() {
  const params = useSearchParams();
  const router = useRouter();
  const queryClient = useQueryClient();

  const status = (params.get("status") as JobStatus) || undefined;
  const page = Number(params.get("page") ?? 1);

  const { data, isLoading } = useQuery({
    queryKey: ["jobs", status, page],
    queryFn: () => getJobs(status, page),
  });

  const cancel = useMutation({
    mutationFn: cancelJob,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });

  return (
    <main className="p-8 max-w-7xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-xl font-semibold text-gray-900">Jobs</h1>
        <Link
          href="/create"
          className="bg-gray-900 text-white text-sm px-4 py-2 rounded hover:bg-gray-700"
        >
          + Create Job
        </Link>
      </div>

      {/* Status filter */}
      <div className="flex gap-2 mb-4 flex-wrap">
        {STATUSES.map((s) => (
          <button
            key={s || "all"}
            onClick={() => router.push(s ? `/jobs?status=${s}` : "/jobs")}
            className={`
              text-xs px-3 py-1 rounded-full border transition-colors
              ${
                (status ?? "") === s
                  ? "bg-gray-900 text-white border-gray-900"
                  : "text-gray-600 border-gray-300 hover:border-gray-600"
              }
            `}
          >
            {s || "all"}
          </button>
        ))}
      </div>

      {isLoading ? (
        <p className="text-gray-400 text-sm">Loading...</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-gray-200">
          <table className="w-full text-sm text-left">
            <thead className="bg-gray-50 text-gray-500 text-xs uppercase">
              <tr>
                {[
                  "ID",
                  "Type",
                  "Priority",
                  "Status",
                  "Retries",
                  "Scheduled",
                  "Interval",
                  "Created",
                  "Actions",
                ].map((h) => (
                  <th key={h} className="px-4 py-3 font-medium">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {data?.items.map((job) => (
                <tr key={job.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-4 py-3 font-mono text-xs text-gray-400">
                    {job.id.slice(0, 8)}…
                  </td>
                  <td className="px-4 py-3 text-gray-700">{job.type}</td>
                  <td className="px-4 py-3">
                    <PriorityBadge priority={job.priority} />
                  </td>
                  <td className="px-4 py-3">
                    <StatusBadge status={job.status} />
                  </td>
                  <td className="px-4 py-3 text-gray-600">
                    {job.retryCount}/{job.maxRetries}
                  </td>
                  <td className="px-4 py-3 text-gray-500 whitespace-nowrap">
                    {new Date(job.scheduledAt).toLocaleString()}
                  </td>
                  <td className="px-4 py-3 text-gray-500">
                    {job.recurrence ?? "—"}
                  </td>
                  <td className="px-4 py-3 text-gray-500 whitespace-nowrap">
                    {new Date(job.createdAt).toLocaleString()}
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex gap-2">
                      <Link
                        href={`/jobs/${job.id}`}
                        className="text-blue-600 hover:underline text-xs"
                      >
                        View
                      </Link>
                      {(job.status === "pending" ||
                        job.status === "processing") && (
                        <button
                          onClick={() => cancel.mutate(job.id)}
                          className="text-red-500 hover:underline text-xs"
                        >
                          Cancel
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {data?.items.length === 0 && (
            <p className="text-center text-gray-400 text-sm py-12">
              No jobs found.
            </p>
          )}
        </div>
      )}

      {/* Pagination */}
      {data && data.total > data.pageSize && (
        <div className="flex justify-end gap-2 mt-4">
          <button
            disabled={page <= 1}
            onClick={() =>
              router.push(
                `/jobs?${status ? `status=${status}&` : ""}page=${page - 1}`,
              )
            }
            className="text-sm px-3 py-1 border rounded disabled:opacity-40"
          >
            Previous
          </button>
          <button
            disabled={data.items.length < data.pageSize}
            onClick={() =>
              router.push(
                `/jobs?${status ? `status=${status}&` : ""}page=${page + 1}`,
              )
            }
            className="text-sm px-3 py-1 border rounded disabled:opacity-40"
          >
            Next
          </button>
        </div>
      )}
    </main>
  );
}

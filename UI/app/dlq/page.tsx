"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getDlq, retryDlqJob } from "@/lib/api";
import { StatusBadge } from "@/components/StatusBadge";

export default function DlqPage() {
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({
    queryKey: ["dlq"],
    queryFn: getDlq,
  });

  const retry = useMutation({
    mutationFn: retryDlqJob,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["dlq"] });
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });

  return (
    <main className="p-8 max-w-5xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-xl font-semibold text-gray-900">
          Dead Letter Queue
        </h1>
        <span className="text-sm text-gray-500">
          {data?.length ?? 0} unresolved
        </span>
      </div>

      {isLoading && <p className="text-gray-400 text-sm">Loading...</p>}

      {!isLoading && data?.length === 0 && (
        <div className="text-center py-16 text-gray-400">
          <p className="text-lg">No failed jobs.</p>
          <p className="text-sm mt-1">
            Jobs exhausting all retries appear here.
          </p>
        </div>
      )}

      <div className="space-y-4">
        {data?.map((entry) => (
          <div
            key={entry.id}
            className="bg-white border border-gray-200 rounded-lg p-5 shadow-sm"
          >
            <div className="flex items-start justify-between gap-4">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-3 mb-2">
                  <span className="font-mono text-xs text-gray-400">
                    {entry.jobId.slice(0, 8)}…
                  </span>
                  {entry.job && <StatusBadge status={entry.job.status} />}
                  <span className="text-xs text-gray-500">
                    {entry.failureCount} failure
                    {entry.failureCount !== 1 ? "s" : ""}
                  </span>
                  <span className="text-xs text-gray-400">
                    {new Date(entry.createdAt).toLocaleString()}
                  </span>
                </div>

                {/* Job details */}
                {entry.job && (
                  <div className="flex gap-4 text-xs text-gray-600 mb-3">
                    <span>
                      Type: <strong>{entry.job.type}</strong>
                    </span>
                    <span>
                      Priority: <strong>{entry.job.priority}</strong>
                    </span>
                  </div>
                )}

                {/* Error details */}
                <div className="bg-red-50 border border-red-100 rounded px-3 py-2">
                  <p className="text-xs font-medium text-red-700 mb-1">
                    Error Details
                  </p>
                  <p className="text-xs text-red-600 font-mono break-words">
                    {entry.errorDetails}
                  </p>
                </div>

                {/* Payload */}
                {entry.job && (
                  <details className="mt-3">
                    <summary className="text-xs text-gray-500 cursor-pointer hover:text-gray-700">
                      View payload
                    </summary>
                    <pre
                      className="mt-2 text-xs bg-gray-50 border border-gray-200
                                    rounded px-3 py-2 overflow-x-auto"
                    >
                      {JSON.stringify(JSON.parse(entry.job.payload), null, 2)}
                    </pre>
                  </details>
                )}
              </div>

              {/* Retry button */}
              <button
                onClick={() => retry.mutate(entry.jobId)}
                disabled={retry.isPending}
                className="shrink-0 bg-gray-900 text-white text-xs
                           px-4 py-2 rounded hover:bg-gray-700
                           disabled:opacity-50 transition-colors"
              >
                {retry.isPending ? "Retrying…" : "Retry"}
              </button>
            </div>
          </div>
        ))}
      </div>
    </main>
  );
}

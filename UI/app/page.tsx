/* eslint-disable @typescript-eslint/no-explicit-any */
"use client";

import { useQuery } from "@tanstack/react-query";
import { getDashboard, JobStatus } from "@/lib/api";
import { useJobEvents } from "@/hooks/useJobEvents";
import Link from "next/link";

const STATUS_ORDER: JobStatus[] = [
  "pending",
  "blocked",
  "processing",
  "completed",
  "failed",
  "cancelled",
];

const STATUS_COLORS: Record<JobStatus, string> = {
  pending: "border-yellow-400",
  blocked: "border-gray-300",
  processing: "border-blue-400",
  completed: "border-green-400",
  failed: "border-red-400",
  cancelled: "border-zinc-300",
};

export default function DashboardPage() {
  useJobEvents(); // SSE connection lives here — top of tree

  const { data, isLoading } = useQuery({
    queryKey: ["dashboard"],
    queryFn: getDashboard,
    refetchInterval: 30_000, // fallback poll
  });

  if (isLoading) {
    return <main className="p-8 text-gray-400">Loading dashboard...</main>;
  }

  const counts: any = data?.statusCounts ?? {};

  return (
    <main className="p-8 max-w-6xl mx-auto">
      <div className="flex items-center justify-between mb-8">
        <h1 className="text-2xl font-semibold text-gray-900">Job Scheduler</h1>
        <nav className="flex gap-4 text-sm">
          <Link href="/" className="text-gray-900 font-medium">
            Dashboard
          </Link>
          <Link href="/jobs" className="text-gray-500 hover:text-gray-900">
            Jobs
          </Link>
          <Link href="/create" className="text-gray-500 hover:text-gray-900">
            Create
          </Link>
          <Link href="/dlq" className="text-gray-500 hover:text-gray-900">
            DLQ
          </Link>
        </nav>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
        {STATUS_ORDER.map((status) => (
          <Link
            key={status}
            href={`/jobs?status=${status}`}
            className={`
              bg-white border-l-4 ${STATUS_COLORS[status]}
              rounded-lg p-5 shadow-sm hover:shadow-md transition-shadow
            `}
          >
            <p className="text-sm text-gray-500 capitalize mb-1">{status}</p>
            <p className="text-3xl font-bold text-gray-900">
              {counts[status] ?? 0}
            </p>
          </Link>
        ))}
      </div>

      <p className="mt-6 text-xs text-gray-400">
        Last updated{" "}
        {data?.generatedAt
          ? new Date(data.generatedAt).toLocaleTimeString()
          : "—"}
        {" · "}Live updates via SSE
      </p>
    </main>
  );
}

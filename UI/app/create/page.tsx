/* eslint-disable @typescript-eslint/no-explicit-any */
"use client";

import { useState } from "react";
import { useMutation, useQueryClient, useQuery } from "@tanstack/react-query";
import {
  createJob,
  getJobs,
  JobType,
  JobPriority,
  JobRecurrence,
} from "@/lib/api";
import { useRouter } from "next/navigation";

const JOB_TYPES: JobType[] = ["sendEmail", "webhookDelivery", "logProcessor"];
const PRIORITIES: JobPriority[] = ["high", "medium", "low"];
const RECURRENCES: JobRecurrence[] = [
  "every1Minute",
  "every5Minutes",
  "every1Hour",
];

const EXAMPLE_PAYLOADS: Record<JobType, string> = {
  sendEmail: JSON.stringify(
    { to: "user@example.com", subject: "Hello", body: "Welcome!" },
    null,
    2,
  ),
  webhookDelivery: JSON.stringify(
    {
      url: "https://example.com/hook",
      method: "POST",
      body: '{"event":"test"}',
    },
    null,
    2,
  ),
  logProcessor: JSON.stringify(
    {
      source: "api-service",
      level: "ERROR",
      message: "Connection refused",
      fields: { host: "10.0.0.1" },
    },
    null,
    2,
  ),
};

export default function CreateJobPage() {
  const router = useRouter();
  const queryClient = useQueryClient();

  const [type, setType] = useState<JobType>("sendEmail");
  const [priority, setPriority] = useState<JobPriority>("medium");
  const [payload, setPayload] = useState(EXAMPLE_PAYLOADS.sendEmail);
  const [scheduledAt, setScheduledAt] = useState("");
  const [recurrence, setRecurrence] = useState<JobRecurrence | "">("");
  const [dependsOn, setDependsOn] = useState<string[]>([]);
  const [payloadErr, setPayloadErr] = useState("");
  const [error, setError] = useState("");

  const { data: existingJobs } = useQuery({
    queryKey: ["jobs"],
    queryFn: () => getJobs(undefined, 1, 100),
  });

  const create = useMutation({
    mutationFn: createJob,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
      router.push("/jobs");
    },
    onError: (err: any) => {
      setError(err?.response?.data?.error ?? "Failed to create job.");
    },
  });

  function validatePayload(value: string): boolean {
    try {
      JSON.parse(value);
      setPayloadErr("");
      return true;
    } catch {
      setPayloadErr("Payload must be valid JSON.");
      return false;
    }
  }

  function handleTypeChange(t: JobType) {
    setType(t);
    setPayload(EXAMPLE_PAYLOADS[t]);
    setPayloadErr("");
  }

  function handleSubmit() {
    setError("");
    if (!validatePayload(payload)) return;

    // Convert local datetime-local value to UTC ISO string
    const scheduledAtUtc = scheduledAt
        ? new Date(scheduledAt).toISOString()
        : undefined;

    create.mutate({
        type,
        payload,
        priority,
        scheduledAt: scheduledAtUtc,
        recurrence: recurrence || undefined,
        dependsOn: dependsOn.length > 0 ? dependsOn : undefined,
    });
}

  return (
    <main className="p-8 max-w-2xl mx-auto">
      <h1 className="text-xl font-semibold text-gray-900 mb-6">Create Job</h1>

      <div className="space-y-5">
        {/* Type */}
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Job Type
          </label>
          <select
            value={type}
            onChange={(e) => handleTypeChange(e.target.value as JobType)}
            className="w-full border border-gray-300 rounded px-3 py-2 text-sm"
          >
            {JOB_TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </div>

        {/* Priority */}
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Priority
          </label>
          <div className="flex gap-3">
            {PRIORITIES.map((p) => (
              <button
                key={p}
                onClick={() => setPriority(p)}
                className={`
                  flex-1 py-2 rounded border text-sm capitalize transition-colors
                  ${
                    priority === p
                      ? "bg-gray-900 text-white border-gray-900"
                      : "border-gray-300 text-gray-600 hover:border-gray-600"
                  }
                `}
              >
                {p}
              </button>
            ))}
          </div>
        </div>

        {/* Payload */}
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Payload (JSON)
          </label>
          <textarea
            value={payload}
            onChange={(e) => {
              setPayload(e.target.value);
              validatePayload(e.target.value);
            }}
            rows={6}
            className={`
              w-full border rounded px-3 py-2 text-sm font-mono resize-y
              ${payloadErr ? "border-red-400" : "border-gray-300"}
            `}
          />
          {payloadErr && (
            <p className="text-xs text-red-500 mt-1">{payloadErr}</p>
          )}
        </div>

        {/* Scheduled At */}
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Scheduled At (optional — leave blank for immediate)
          </label>
          <input
            type="datetime-local"
            value={scheduledAt}
            onChange={(e) => setScheduledAt(e.target.value)}
            className="w-full border border-gray-300 rounded px-3 py-2 text-sm"
          />
        </div>

        {/* Recurrence */}
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Recurrence (optional)
          </label>
          <select
            value={recurrence}
            onChange={(e) =>
              setRecurrence(e.target.value as JobRecurrence | "")
            }
            className="w-full border border-gray-300 rounded px-3 py-2 text-sm"
          >
            <option value="">None</option>
            {RECURRENCES.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
        </div>

        {/* Dependencies */}
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Depends On (optional — select jobs that must complete first)
          </label>
          <div className="max-h-40 overflow-y-auto border border-gray-300 rounded p-2 space-y-1">
            {existingJobs?.items.length === 0 && (
              <p className="text-xs text-gray-400">No existing jobs.</p>
            )}
            {existingJobs?.items.map((job) => (
              <label
                key={job.id}
                className="flex items-center gap-2 text-xs cursor-pointer"
              >
                <input
                  type="checkbox"
                  checked={dependsOn.includes(job.id)}
                  onChange={(e) =>
                    setDependsOn((prev) =>
                      e.target.checked
                        ? [...prev, job.id]
                        : prev.filter((id) => id !== job.id),
                    )
                  }
                />
                <span className="font-mono text-gray-500">
                  {job.id.slice(0, 8)}…
                </span>
                <span className="text-gray-700">{job.type}</span>
                <span className="text-gray-400">({job.status})</span>
              </label>
            ))}
          </div>
        </div>

        {error && (
          <p className="text-sm text-red-500 bg-red-50 px-3 py-2 rounded">
            {error}
          </p>
        )}

        <button
          onClick={handleSubmit}
          disabled={create.isPending || !!payloadErr}
          className="w-full bg-gray-900 text-white py-2 rounded text-sm
                     hover:bg-gray-700 disabled:opacity-50 transition-colors"
        >
          {create.isPending ? "Creating…" : "Create Job"}
        </button>
      </div>
    </main>
  );
}

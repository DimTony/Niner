import { JobStatus } from "@/lib/api";

const styles: Record<JobStatus, string> = {
  pending: "bg-yellow-100 text-yellow-800",
  blocked: "bg-gray-100 text-gray-600",
  processing: "bg-blue-100 text-blue-800",
  completed: "bg-green-100 text-green-800",
  failed: "bg-red-100 text-red-800",
  cancelled: "bg-zinc-100 text-zinc-500",
};

export function StatusBadge({ status }: { status: JobStatus }) {
  return (
    <span className={`px-2 py-1 rounded text-xs font-medium ${styles[status]}`}>
      {status}
    </span>
  );
}

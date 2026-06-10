import { JobPriority } from "@/lib/api";

const styles: Record<JobPriority, string> = {
  high: "bg-red-50 text-red-700",
  medium: "bg-orange-50 text-orange-700",
  low: "bg-slate-50 text-slate-600",
};

export function PriorityBadge({ priority }: { priority: JobPriority }) {
  return (
    <span
      className={`px-2 py-1 rounded text-xs font-medium ${styles[priority]}`}
    >
      {priority}
    </span>
  );
}

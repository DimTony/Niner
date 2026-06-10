import JobsPageClient from "@/components/JobsPageClient";
import { Suspense } from "react";

export default function Page() {
  return (
    <Suspense fallback={<div className="p-8">Loading...</div>}>
      <JobsPageClient />
    </Suspense>
  );
}
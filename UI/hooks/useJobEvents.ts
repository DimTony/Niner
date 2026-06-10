"use client";

import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";

interface JobEvent {
  jobId: string;
  status: string;
  timestamp: string;
}

export function useJobEvents() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const url = `${process.env.NEXT_PUBLIC_API_URL}/api/events/stream`;
    const es = new EventSource(url, { withCredentials: true });

    es.addEventListener("job_update", (e) => {
      const event: JobEvent = JSON.parse(e.data);

      // Invalidate only the affected queries — not a full refetch
      queryClient.invalidateQueries({ queryKey: ["job", event.jobId] });
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });

      if (event.status === "failed")
        queryClient.invalidateQueries({ queryKey: ["dlq"] });
    });

    es.addEventListener("connected", () => {
      console.info("[SSE] Stream connected.");
    });

    es.onerror = () => {
      // EventSource retries automatically — no manual reconnect needed
      console.warn("[SSE] Stream error, retrying...");
    };

    return () => es.close();
  }, [queryClient]);
}

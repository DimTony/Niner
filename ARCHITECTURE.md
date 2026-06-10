# ARCHITECTURE.md

# Distributed Job Scheduler Architecture

## System Overview

This system is a distributed job scheduler built around three independently running processes:

1. **API**
2. **Scheduler**
3. **Workers**

Each process has a single responsibility.

```
                    ┌─────────────────┐
                    │     Next.js     │
                    │       UI        │
                    └────────┬────────┘
                             │
                             ▼
                    ┌─────────────────┐
                    │    .NET API     │
                    │ REST + SSE      │
                    └───────┬─────────┘
                            │
          ┌─────────────────┼─────────────────┐
          ▼                                   ▼
 ┌─────────────────┐               ┌─────────────────┐
 │   PostgreSQL    │               │      Redis      │
 │ Source of Truth │               │ Operational DB  │
 └─────────────────┘               └─────────────────┘
                                            ▲
                                            │
                   ┌────────────────────────┼────────────────────┐
                   ▼                        ▼                    ▼
          ┌────────────────┐      ┌────────────────┐   ┌────────────────┐
          │   Scheduler    │      │   Worker #1    │   │   Worker #N    │
          └────────────────┘      └────────────────┘   └────────────────┘
```

### Responsibilities

#### API

Responsible for:

* Job creation
* Job cancellation
* Job queries
* DLQ management
* DAG submission
* SSE streaming

The API never executes jobs.

#### Scheduler

Responsible for:

* Promoting scheduled jobs
* Aging/starvation prevention
* Timing wheel management

The scheduler never executes jobs.

#### Workers

Responsible for:

* Claiming jobs
* Executing handlers
* Retry logic
* DLQ routing

Workers never schedule jobs.

---

# Storage Architecture

## PostgreSQL

PostgreSQL is the permanent source of truth.

All job state exists here:

* Job definitions
* Status
* Retries
* DAG dependencies
* Error history
* DLQ entries
* Audit logs

If Redis is lost, the entire system can be rebuilt from PostgreSQL.

## Redis

Redis is the operational layer.

Stores only:

* Ready queue
* Scheduled queue
* Timing wheel slots
* Worker locks
* Pub/Sub events
* DLQ alert counters

Redis data is considered disposable.

---

# Job Lifecycle

## State Machine

```text
             ┌──────────┐
             │ Blocked  │
             └────┬─────┘
                  │
                  ▼
             ┌──────────┐
             │ Pending  │
             └────┬─────┘
                  │
                  ▼
             ┌──────────┐
             │Processing│
             └─┬──┬──┬──┘
               │  │  │
      Success  │  │  │ Failure
               │  │  ▼
               │  │ Failed
               │  │
               │  │ Retry
               │  ▼
               │ Pending
               │
               ▼
          Completed

Cancellation:
Pending -> Cancelled
Processing -> Cancelled (checkpoint-based)
```

## Status Definitions

| Status     | Meaning                   |
| ---------- | ------------------------- |
| Blocked    | Waiting for dependencies  |
| Pending    | Eligible to run           |
| Processing | Worker currently owns job |
| Completed  | Finished successfully     |
| Failed     | Exhausted retries         |
| Cancelled  | Explicitly cancelled      |

---

# Heap Implementation

## Purpose

The heap determines execution order among ready jobs.

Workers always consume the job with the smallest score.

## Score Formula

```text
score =
    (effective_priority × 1_000_000_000_000)
    + scheduled_at_ms
    + created_at_ms / 1_000_000
```

Lower score wins.

Priority ordering:

```text
High   = 1
Medium = 2
Low    = 3
```

Example:

```text
High Priority:
1,734,000,000

Medium Priority:
1,001,734,000,000
```

The priority component dominates all timestamps.

## Heap Operations

| Operation  | Complexity |
| ---------- | ---------- |
| Push       | O(log n)   |
| Pop        | O(log n)   |
| Peek       | O(1)       |
| Build Heap | O(n)       |

## Redis Mirror

The heap is mirrored into Redis using:

```text
scheduler:ready_queue
```

Redis Sorted Set score equals heap score.

Workers consume jobs using:

```redis
ZPOPMIN scheduler:ready_queue
```

This guarantees consistent ordering across all worker instances.

---

# Timing Wheel

## Purpose

Efficient scheduling of near-future jobs.

Reduces repeated scans of scheduled jobs.

## Structure

```text
scheduler:wheel:slot:0
scheduler:wheel:slot:1
...
scheduler:wheel:slot:3599
```

3600 slots.

One slot per second.

Represents a rolling one-hour window.

## Slot Calculation

```text
slot =
scheduled_at_unix_seconds % 3600
```

Example:

```text
scheduled_at = 1717001234

slot = 1717001234 % 3600
     = 1234
```

Job is inserted into:

```text
scheduler:wheel:slot:1234
```

## Tick Processing

Every second:

```text
current_slot = (current_slot + 1) % 3600
```

Scheduler:

1. Reads current slot
2. Moves due jobs to ready queue
3. Clears slot

## Complexity

| Operation | Complexity |
| --------- | ---------- |
| Insert    | O(1)       |
| Tick      | O(k)       |
| Remove    | O(1)       |

k = jobs in current slot

---

# Benchmark Results

BenchmarkDotNet results collected using 100,000 scheduled jobs.

## Heap

| Operation | Mean    |
| --------- | ------- |
| Push      | 0.84 µs |
| Pop       | 1.12 µs |
| Peek      | 0.02 µs |

## Timing Wheel

| Operation         | Mean    |
| ----------------- | ------- |
| Insert            | 0.03 µs |
| Tick (empty slot) | 0.01 µs |
| Tick (100 jobs)   | 0.27 µs |

## Observations

Heap performance scales logarithmically.

Timing wheel insertion remains constant regardless of queue size.

Timing wheel significantly outperforms heap for large numbers of short-term scheduled jobs.

---

# Starvation Prevention

## Problem

High-priority jobs can indefinitely delay low-priority jobs.

## Aging Policy

Waiting time boosts effective priority.

| Wait Time | Effective Priority |
| --------- | ------------------ |
| < 2 hours | Original           |
| ≥ 2 hours | Medium             |
| ≥ 4 hours | High               |

Actual priority stored in PostgreSQL never changes.

Only queue position changes.

## Effective Priority Function

```text
if wait >= 4h:
    High

else if wait >= 2h:
    Medium

else:
    ActualPriority
```

Scheduler periodically recalculates scores and updates Redis.

---

# DAG Execution Model

## Dependency Storage

Dependencies are stored as an adjacency list.

```text
job_dependencies

job_id
depends_on_id
```

Example:

```text
Job C depends on A and B

C -> A
C -> B
```

## Execution Rule

A job becomes runnable only when every dependency is completed.

## Unblocking Query

When a job completes:

```sql
SELECT j.id
FROM jobs j
JOIN job_dependencies d
    ON d.job_id = j.id
WHERE d.depends_on_id = :completed_job
AND j.status = 'blocked'
AND NOT EXISTS (
    SELECT 1
    FROM job_dependencies d2
    JOIN jobs j2
        ON j2.id = d2.depends_on_id
    WHERE d2.job_id = j.id
      AND j2.status != 'completed'
);
```

Returned jobs transition:

```text
Blocked -> Pending
```

and enter the ready queue.

No polling required.

---

# Duplicate Protection

## Problem

Multiple workers may attempt to process the same job.

## Lock Acquisition

Workers use Redis locks.

```redis
SET lock:job:{job_id}
    {worker_id}
    NX
    PX 30000
```

### Meaning

```text
NX = only if key does not exist
PX = expire after 30 seconds
```

If lock creation succeeds:

```text
Worker owns job
```

Otherwise:

```text
Another worker owns job
```

## Heartbeat

Every 10 seconds:

```redis
PEXPIRE lock:job:{job_id} 30000
```

Lock remains alive while processing.

## Atomic Release

Lua script:

```lua
if redis.call("GET", KEYS[1]) == ARGV[1]
then
    return redis.call("DEL", KEYS[1])
else
    return 0
end
```

Prevents accidental deletion of another worker's lock.

## Worker Crash

If worker dies:

1. Heartbeat stops
2. Lock expires
3. Job becomes reclaimable

No manual recovery required.

---

# Retry Strategy

## Retry Policy

Maximum retries:

```text
3
```

## Backoff Formula

```text
delay = base_delay + jitter
```

Base delays:

```text
Attempt 1 -> 1 second
Attempt 2 -> 5 seconds
Attempt 3 -> 25 seconds
```

Jitter:

```text
0% to 20% of base delay
```

Example:

```text
5s + random(0s..1s)
```

## Rationale

Jitter prevents synchronized retry storms when many jobs fail simultaneously.

---

# Cancellation Model

## Pending Job

Immediately cancelled.

Actions:

1. Update PostgreSQL
2. Remove from Redis queue

State transition:

```text
Pending -> Cancelled
```

## Processing Job

Workers are not forcefully terminated.

API sets:

```text
cancellation_requested = true
```

Worker checks cancellation after each logical checkpoint.

When detected:

1. Cleanup
2. Release resources
3. Mark cancelled

State transition:

```text
Processing -> Cancelled
```

This prevents partial writes and data corruption.

---

# Dead Letter Queue (DLQ)

## Entry Conditions

Job enters DLQ when:

```text
retry_count >= max_retries
```

## Storage

```text
dead_letter_queue
```

Stores:

* Job reference
* Failure count
* Error details
* Resolution state

## Alert Threshold

```text
10 unresolved failures
```

Redis counter:

```text
dlq:count
```

Workflow:

```text
INCR dlq:count

if count >= 10:
    send alert
    reset counter
```

## Alert Mechanism

Current implementation:

* Structured warning log
* Mock email notification

---

# Live Updates

## Architecture

```text
Worker
  │
  ▼
Redis Pub/Sub
(job:events)
  │
  ▼
Hosted SSE Service
  │
  ▼
Browser EventSource
```

## Event Flow

Worker publishes:

```json
{
  "jobId": "...",
  "status": "Completed"
}
```

Redis channel:

```text
job:events
```

API-hosted subscriber receives event.

Subscriber broadcasts via SSE.

Browser updates only affected row.

No polling.

No page refresh.

---

# Deployment

## Server Layout

```text
VPS
├── Nginx
├── .NET API
├── .NET Scheduler
├── .NET Worker
├── Next.js
├── PostgreSQL
└── Redis
```

## Networking

### Public

```text
443 HTTPS
80 HTTP (redirect)
```

### Internal

```text
API         :5000
Next.js     :3000
Postgres    :5432
Redis       :6379
```

PostgreSQL and Redis are bound to localhost only.

## Reverse Proxy

Nginx handles:

* SSL termination
* Reverse proxy
* Compression
* SSE forwarding

## DNS

DuckDNS:

```text
scheduler.duckdns.org
```

## TLS

Certbot:

```text
Let's Encrypt
```

Automatic renewal enabled.

## Services

```text
jobscheduler-api.service
jobscheduler-worker.service
jobscheduler-scheduler.service
```

Managed by systemd.

Automatic restart on failure.

---

# Design Principles

1. PostgreSQL is the source of truth.
2. Redis is disposable operational state.
3. API never executes jobs.
4. Scheduler never executes jobs.
5. Workers never schedule jobs.
6. Locks prevent duplicate execution.
7. DAG resolution is event-driven.
8. Starvation is prevented through aging.
9. Failed jobs are recoverable through DLQ workflows.
10. Real-time visibility is provided through SSE.

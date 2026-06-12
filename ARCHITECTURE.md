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
* Recovery of orphaned scheduled jobs

The scheduler never executes jobs.

#### Workers

Responsible for:

* Claiming jobs
* Executing handlers
* Retry logic
* DLQ routing
* Recovery of stale locks from crashed workers
* Scheduling recurring job instances

Workers never schedule the initial promotion of a job, but they do create and enqueue the next instance of a recurring job after completion.

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
* Worker heartbeats

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
        ┌───►│ Pending  │◄───┐
        │    └────┬─────┘    │
        │         │          │
        │         ▼          │
        │    ┌──────────┐    │
        └────┤Processing├────┘
   Re-blocked └─┬──┬──┬──┘  Retry / Stale-lock recovery
   (deps not    │  │  │
    yet done)   │  │  │ Failure
       Success  │  │  ▼
                │  │ Failed
                │  │
                │  ▼
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
| Failed     | Exhausted retries          |
| Cancelled  | Explicitly cancelled       |

## Re-blocking on Dequeue

When a worker dequeues a job, it re-checks the job's dependencies before claiming it. If any dependency is not yet `Completed` (a race between promotion and DAG resolution), the job transitions back from `Pending` to `Blocked` and is released without processing. It re-enters the ready queue automatically once `UnblockDependents` fires for the outstanding dependency.

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

The priority component dominates all timestamps. The `created_at_ms / 1_000_000` term acts as a tiebreaker between jobs with identical priority and `scheduled_at`, favoring earlier-created jobs.

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

Waiting time boosts effective priority. Wait time is measured from `created_at`.

| Wait Time   | Effective Priority                          |
| ----------- | -------------------------------------------- |
| < 5 minutes | Original                                      |
| ≥ 5 minutes | Medium (only if original priority is Low)    |
| ≥ 10 minutes| High                                          |

Actual priority stored in PostgreSQL never changes.

Only queue position (score) changes.

## Effective Priority Function

```text
if wait >= 10 minutes:
    High

else if wait >= 5 minutes:
    if actual == Low:
        Medium
    else:
        actual

else:
    actual
```

Scheduler periodically recalculates scores and updates Redis (and the in-memory heap mirror) on a fixed interval (`AgingIntervalSeconds`).

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

## Unblocking on Completion

When a job completes, the worker looks up all jobs that depend on it, then for each candidate checks whether it still has any incomplete dependency:

1. Find all `job_dependencies` rows where `depends_on_id` equals the completed job's ID — these are the candidate dependent jobs.
2. For each candidate, check whether any of its dependencies are not yet `Completed`.
3. If none are outstanding and the candidate's status is `Blocked`, transition it to `Pending` and push it onto the ready queue.

```text
Blocked -> Pending
```

This is event-driven (triggered by job completion) rather than polled, though it is implemented as one query per candidate dependent rather than a single set-based SQL statement.

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

While a job is in `Processing`, the owning worker renews its lock on an interval (`HeartbeatIntervalSeconds`):

```redis
PEXPIRE lock:job:{job_id} 30000
```

The renewal is performed via a Lua script that checks the lock value matches the renewing worker's ID before extending the TTL, so a worker can never renew a lock it no longer owns.

The heartbeat task is started after a job is claimed and is cancelled at each cancellation checkpoint (before handler execution, and after the handler completes).

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

If a worker dies mid-processing, its lock heartbeat stops and the Redis lock eventually expires. Independently, the job row in PostgreSQL still shows `Processing` with a stale `LockedAt` timestamp — see Recovery & Self-Healing below for how this is reclaimed.

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
±20% of base delay
```

Example:

```text
5s ± up to 1s
```

## Rationale

Jitter prevents synchronized retry storms when many jobs fail simultaneously.

---

# Cancellation Model

## Pending Job

Immediately cancelled.

Actions:

1. Update PostgreSQL
2. Remove from Redis queue (both ready and scheduled sets)

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

(represented by the job's `Status` field being set to `Cancelled` directly)

The worker checks for cancellation at logical checkpoints:

1. Immediately after claiming the job and before invoking the handler
2. Immediately after the handler completes, before recording success or failure

When detected:

1. Cancel the lock heartbeat
2. Log the cancellation
3. Publish a `cancelled` event
4. Release the Redis lock
5. Discard the handler's result

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
retry_count > max_retries
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

if count >= threshold:
    send alert
    reset counter
```

## Alert Mechanism

Current implementation:

* Structured warning log
* Mock email notification

## Manual Retry

A DLQ entry can be manually retried via the API. This resets the job's retry count, status, lock fields, and error to a clean `Pending` state, marks the DLQ entry resolved, decrements the DLQ counter, and re-enqueues the job into the ready queue with a freshly computed score.

---

# Recurring Jobs

## Purpose

Some job types need to run on a fixed schedule indefinitely (e.g. periodic health checks, polling jobs).

## Supported Intervals

```text
Every1Minute
Every5Minutes
Every1Hour
```

## Flow

Recurrence is driven entirely by the worker, on successful completion of a job that has a `Recurrence` value set:

1. The current job completes normally and is marked `Completed`.
2. The worker computes the next run time as `now + interval`.
3. A brand new job row is created with:
   - A new `Id`
   - The same `Type`, `Payload`, `Priority`, `MaxRetries`, and `Recurrence` as the completed job
   - `Status = Pending`, `RetryCount = 0`
   - `ScheduledAt` set to the computed next run time
4. The new job is inserted into PostgreSQL and added to the Redis scheduled set (`scheduler:scheduled_jobs`) keyed by its `ScheduledAt`.
5. A `Created` log entry is written against the new job, recording the parent job's ID and the computed interval in its metadata.

## Notes

* There is no foreign key or persistent parent/child relationship between recurring job instances — the link exists only in the new job's log metadata.
* If the parent job is cancelled or fails permanently (moves to DLQ), no further instances are scheduled, since recurrence only fires on the success path.
* Each instance goes through the full normal lifecycle (timing wheel placement, promotion, aging, DAG checks if dependencies are ever added, etc.) independently.

---

# Recovery & Self-Healing

The system has two independent, time-based recovery mechanisms that act as safety nets on top of the primary event-driven flows. Both are designed so that PostgreSQL remains the authority and Redis state can always be reconstructed or reconciled from it.

## Stale Lock Recovery (Worker)

Runs on each worker on a fixed interval (every 30 seconds).

1. Query PostgreSQL for jobs in `Processing` status whose `LockedAt` is older than `StaleLockThresholdMinutes`.
2. For each such job:
   - Reset `Status` to `Pending`, clear `LockedBy` and `LockedAt`.
   - Recompute its score and push it back onto the Redis ready queue.
   - Write a `RetryAttempted` log entry noting the recovery.

This handles the case where a worker crashes or is killed while holding a job, leaving the Redis lock to expire naturally while the PostgreSQL row is left stuck in `Processing`.

## Orphan Scheduled-Job Recovery (Scheduler)

Runs on the scheduler on a fixed interval (every 60 seconds), starting 10 seconds after scheduler startup to let normal promotion run first.

1. Query PostgreSQL for all jobs that are `Pending` and whose `ScheduledAt` has already passed.
2. For each such job, attempt `EnqueueReadyIfAbsent` — a conditional (`NX`-style) add to the Redis ready queue that only succeeds if the job is not already present.
3. If the add succeeds (the job was missing from Redis), log a warning noting the recovery.

This handles cases where a job's promotion from the scheduled set to the ready queue failed or was lost (e.g. a transient Redis enqueue failure during retry scheduling), without double-enqueuing jobs that are already correctly queued.

## Startup State Restoration (Scheduler)

On startup, the scheduler:

1. Reads the persisted timing wheel pointer from Redis and advances its in-memory wheel to match.
2. Queries PostgreSQL for jobs due within the next hour.
3. For jobs whose `ScheduledAt` is still in the future, re-adds them to the Redis scheduled set and the in-memory timing wheel.
4. For jobs already due, computes their score, pushes them onto the Redis ready queue, and pushes them onto the in-memory heap.

This ensures the scheduler's in-memory heap and timing wheel are rebuilt consistently after a restart.

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

On connection, the API immediately sends a `connected` event so the browser knows the stream is live:

```text
event: connected
data: {"message":"SSE stream connected"}
```

Worker publishes job status changes:

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

API-hosted subscriber receives event and forwards it as a `job_update` SSE event.

Browser updates only affected rows (job detail, jobs list, dashboard counts, and DLQ list when the status is `failed`).

No polling required for updates, though the dashboard also performs a periodic fallback refetch.

---

# Deployment

## Server Layout

```text
AWS EC2 instance
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

PostgreSQL and Redis are bound to localhost only. EC2 security group restricts inbound traffic to 80/443 (and SSH for administration).

## Reverse Proxy

Nginx handles:

* SSL termination
* Reverse proxy
* Compression
* SSE forwarding (buffering disabled for the events stream)

## DNS

DuckDNS:

```text
scheduler.duckdns.org
```

Pointed at the EC2 instance's public IP.

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

Managed by systemd on the EC2 instance. Automatic restart on failure.

---

# Design Principles

1. PostgreSQL is the source of truth.
2. Redis is disposable operational state.
3. API never executes jobs.
4. Scheduler never executes jobs.
5. Workers claim and execute jobs, and schedule the next instance of recurring jobs on success.
6. Locks prevent duplicate execution.
7. DAG resolution is event-driven, triggered on job completion.
8. Starvation is prevented through aging based on wait time since creation.
9. Failed jobs are recoverable through DLQ workflows.
10. Stale locks and orphaned scheduled jobs are reconciled automatically via time-based recovery loops.
11. Real-time visibility is provided through SSE.

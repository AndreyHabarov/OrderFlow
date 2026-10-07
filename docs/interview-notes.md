# Interview notes

Working log. For each problem: situation, cause, solution, numbers before/after, what I learned.
Weak spots found in interview rounds are listed per stage.

## Template: problem entry

### <short title>
- **Situation:**
- **Cause:**
- **Solution:**
- **Numbers (before -> after):**
- **What I learned:**

## Stage 0: Foundation

### Questions to be able to answer
- Why a healthcheck, and what does `depends_on: condition: service_healthy` change?
- Liveness vs readiness: what does each check and who consumes it?
- What does a multi-stage Dockerfile do and why does the restore layer come first?
- Why pin image versions instead of `latest`?
- Why central package management and `TreatWarningsAsErrors`?

### Weak spots (from interview rounds)
- _empty_

### Problems and metrics
- _empty_

## Stage 1: Monolith without a broker

### Questions to be able to answer
- Cache-aside vs read-through vs write-through: which one is used here and why?
- How is the catalog cache invalidated without scanning Redis? What happens to old keys?
- What happens to the API when Redis is down? Is that the right behaviour?
- What is a cache stampede and how would you reduce it?
- Why is MediatR pinned to 12.5.0?

### Problems and metrics
- Catalog list endpoint via `127.0.0.1`: about 5 ms with a warm cache (measured locally, 3 requests). Cold-path number to be recorded with k6 in stage 5b.
- Runtime crash `FileNotFoundException: Microsoft.EntityFrameworkCore 10.0.12`: the code compiled against 10.0.12 (Design package) while Npgsql pulled 10.0.4 at runtime. Fixed by pinning EF Core packages explicitly. Lesson: transitive version drift between compile time and runtime.

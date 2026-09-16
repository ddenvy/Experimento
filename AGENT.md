# Code Standards

## Core Development Principles

### 1. Code Quality and Readability

- **1.1 Semantic Naming**:
    - Names reflect intent, not implementation details
    - Consistent naming style across the entire project
    - Avoid abbreviations unless they are widely accepted

- **1.2 Structure and Organization**:
    - Single Responsibility Principle
    - Modular/package organization by feature or domain
    - Predictable project structure

- **1.3 Formatting and Style**:
    - Auto-formatting with tooling
    - Consistent indentation, line breaks, and import organization
    - Maximum line length of 80–120 characters

### 2. Architectural Principles

- **2.1 Clean Architecture**:
    - Layered separation (presentation, business logic, data)
    - Dependencies point inward, toward the system core
    - Independence from external frameworks and libraries

- **2.2 Design Principles**:
    - DRY (Don't Repeat Yourself) — eliminate duplication
    - KISS (Keep It Simple) — prefer simple implementations
    - YAGNI (You Aren't Gonna Need It) — implement only what is required
    - SOLID principles for OOP languages — apply only when they do not conflict with data locality or measurable performance needs; prefer DOD for hot paths (see 2.4)

- **2.3 Separation of Concerns**:
    - Clear boundaries between components
    - Minimize coupling
    - Maximize cohesion within modules

- **2.4 Data-Oriented Design (DOD) — "Clean Code, Horrible Performance" (Casey Muratori)**:
    - **Flat data over polymorphism**: Replace virtual/abstract class hierarchies with flat `struct` + `enum`. Use an enum `AgentType` plus a static lookup table for per-type parameters instead of `virtual void Update()` / `abstract void Act()` overrides.
    - **Data locality (SoA/AoS)**: Store entities as value types in contiguous buffers — `List<Agent>`, `Agent[]`, or `Span<Agent>` — rather than scattered `class` instances on the heap. This eliminates MethodTable virtual dispatch and CPU cache misses, and lets the JIT auto-vectorize the hot loop (SIMD/AVX via `System.Numerics.Vector<T>` or runtime auto-vectorization).
    - **Data-driven behavior tables**: Move fixed coefficients, weights, speeds, power modifiers, and state transitions into `static readonly` lookup arrays instead of hiding them behind virtual getters or nested helper functions.
    - **Batch processing in a single loop**: Process the whole collection with one flat `for` loop over contiguous memory. No per-object virtual dispatch, no branching on type inside the loop — parameters come directly from the lookup table indexed by the enum.
    - **When to apply**: DOD is mandatory for hot paths (agent/game loops, ECS, physics, rendering, media processing). In non-hot, I/O-bound, or business-logic layers, standard OOP with `class` is acceptable — always profile before switching paradigms.
    - **Reference C# pattern**:

      ```csharp
      using System.Numerics;
      using System.Runtime.CompilerServices;

      // Enum replaces polymorphic base classes
      public enum AgentType : byte
      {
          Worker,
          Scout,
          Guard,
          Count
      }

      // Lookup table for per-type coefficients
      public readonly struct AgentTypeData
      {
          public readonly float SpeedModifier;
          public readonly float PowerModifier;
          public readonly byte Priority;

          public AgentTypeData(float speed, float power, byte priority)
          {
              SpeedModifier = speed;
              PowerModifier = power;
              Priority = priority;
          }
      }

      internal static class AgentTypeRegistry
      {
          // static readonly = precomputed at module load, no per-call cost
          public static readonly AgentTypeData[] Table =
          [
              new(1.0f, 0.5f, 1),  // Worker
              new(2.5f, 0.1f, 3),  // Scout
              new(0.8f, 2.0f, 2)   // Guard
          ];
      }

      // Flat value type — no MethodTable, no hidden state, contiguous in List<Agent>
      public struct Agent
      {
          public float X, Y;
          public float Vx, Vy;
          public float Energy;
          public AgentType Type;
      }

      // Single batch-processing loop — JIT can auto-vectorize (SIMD/AVX)
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static void UpdateAllAgents(Span<Agent> agents, float dt)
      {
          ref Agent start = ref MemoryMarshal.GetReference(agents);
          int count = agents.Length;
          ref AgentTypeData table = ref AgentTypeRegistry.Table[0];

          for (int i = 0; i < count; i++)
          {
              ref Agent agent = ref Unsafe.Add(ref start, i);
              ref readonly AgentTypeData td = ref Unsafe.Add(ref table, (int)agent.Type);

              agent.X += agent.Vx * td.SpeedModifier * dt;
              agent.Y += agent.Vy * td.SpeedModifier * dt;
              agent.Energy -= 0.01f * td.PowerModifier * dt;
          }
      }
      ```
    - **C# DOD guardrails**:
        - Use `struct` (value type) for hot-path entities, **not** `class` — `class` = heap allocation + MethodTable + pointer chasing.
        - Prefer `Span<T>` / `ReadOnlySpan<T>` parameters over `List<T>` — zero-overhead slicing, no interface dispatch.
        - `Unsafe.Add` + `ref` is a zero-overhead alternative to `agents[i]` for JIT; keep the loop body branch-free.
        - For explicit SIMD, use `Vector4` / `Vector<T>` from `System.Numerics`; rely on JIT auto-vectorization for simplicity when possible.
        - Avoid `virtual`, `abstract`, interfaces, and delegates on hot paths — every indirection is a missed optimization opportunity.

## Security and Data Protection

### 3. General Security Requirements

- **3.1 Authentication and Authorization**:
    - Centralized access control
    - Principle of least privilege
    - Regular access rights audit

- **3.2 Data Protection**:
    - Encrypt sensitive data at rest and in transit
    - Mask sensitive values in logs
    - Regular backups

- **3.3 Attack Prevention**:
    - Validate and sanitize all input
    - Protect against injection attacks (SQL, NoSQL, OS command)
    - Protect against XSS, CSRF, and SSRF
    - Rate-limit public APIs

## Data Handling

### 4. State Management

- **4.1 Data Consistency**:
    - Single Source of Truth
    - Predictable state update patterns
    - Optimistic updates with rollback on failure

- **4.2 Caching**:
    - Cache invalidation strategies
    - Cache segmentation by data type
    - Monitor cache effectiveness

### 5. API Design

- **5.1 API Design**:
    - RESTful or GraphQL with consistent conventions
    - API versioning
    - Backward compatibility when making changes

- **5.2 API Contracts**:
    - Formal specification (OpenAPI, GraphQL Schema)
    - Generate client code from specifications
    - Validate requests/responses against the contract

## Testing and Quality

### 6. Testing Strategy

- **6.1 Test Pyramid**:

    ```
    Many unit tests       ↑
    Fewer integration tests ↑
    Even fewer E2E tests  ↑
    ```

- **6.2 Unit Testing**:
    - Isolated testing of individual components
    - Mock/stub external dependencies
    - Cover critical business logic

- **6.3 Integration Testing**:
    - Test component interactions
    - Use test containers for external services
    - Verify contracts between services

- **6.4 E2E Testing**:
    - Test key user scenarios
    - Simulate real-world usage
    - Verify end-to-end integration of all system components

### 7. Monitoring and Observability

- **7.1 Logging**:
    - Structured logging in a machine-readable format
    - Log levels (DEBUG, INFO, WARN, ERROR)
    - Correlation IDs for request tracing

- **7.2 Metrics**:
    - Business and technical metrics
    - Monitor performance and availability
    - Alerting based on metrics

- **7.3 Tracing**:
    - Distributed request tracing
    - Analyze performance across the call chain

## Development and DevOps

### 8. Version Control

- **8.1 Branching Strategy**:
    - GitFlow, GitHub Flow, or Trunk-based development
    - Semantic Versioning (SemVer)
    - Clean commit history

- **8.2 Code Review**:
    - Mandatory review before merge
    - Constructive feedback
    - Automated checks (linters, tests)

### 9. Continuous Integration and Delivery

- **9.1 CI Pipeline**:

    ```yaml
    stages:
      - lint     # Code style checks
      - test     # Run test suite
      - build    # Build artifacts
      - security # Security scans
      - deploy   # Deploy to staging
    ```

- **9.2 CD Strategy**:
    - Blue-green deployments or canary releases
    - Automatic rollback on detected failures
    - Phased rollouts to reduce risk

### 10. Containerization and Orchestration

- **10.1 Docker Best Practices**:
    - Minimal base images
    - Multi-stage builds
    - Non-root users

- **10.2 Orchestration**:
    - Declarative configuration
    - Health checks and readiness probes
    - Auto-scaling

## Documentation and Communication

### 11. Technical Documentation

- **11.1 Documentation Types**:
    - Architecture Decision Records (ADR)
    - API documentation
    - Onboarding guides
    - Operational runbooks

- **11.2 Code Documentation**:
    - Comment the "why", not the "what"
    - README files for modules
    - Usage examples

### 12. Cross-Team Collaboration

- **12.1 Design**:
    - Design reviews before implementation
    - API-first approach
    - Prototype complex features

- **12.2 Communication**:
    - Regular cross-team syncs
    - Shared Definition of Done
    - Transparent task status

## Performance and Optimization

### 13. Performance Optimization

- **13.1 Backend Optimization**:
    - Profile and optimize slow queries
    - Use database indexes effectively
    - Cache results of expensive computations

- **13.2 Frontend Optimization**:
    - Optimize resource loading
    - Lazy-load non-critical functionality
    - Optimize images and media assets

- **13.3 Network Optimization**:
    - HTTP/2 or HTTP/3
    - Gzip/Brotli compression
    - CDN for static assets

## Universal Code Review Checklist

### Code Quality

- Code follows project conventions
- No duplication
- Appropriate use of design patterns
- Reasonable complexity (cyclomatic, cognitive)

### Security

- Input validation
- No OWASP Top 10 vulnerabilities
- Secure secret storage
- Correct authentication/authorization handling

### Testing

- Adequate test coverage
- Tests are isolated and reproducible
- Boundary cases are tested
- Error handling is tested

### Performance

- No performance bottlenecks
- Optimal resource usage
- Correct caching
- Efficient algorithms and data structures
- Hot paths use data-oriented design: contiguous storage, flat loops, no virtual dispatch, lookup-table-driven parameters (see 2.4)
- Polymorphism on hot paths is justified by measurement, not habit

### Scalability

- Code is ready for horizontal scaling
- No blocking operations
- Correct behavior in a distributed environment

### Documentation

- API documentation updated
- README files updated
- Clear comments for complex logic
- Usage examples

## Quality Metrics

### 14. Quantitative Metrics

- **14.1 Code Quality**:
    - Code coverage (target: 80%+ for critical components)
    - Static analysis issues (target: 0 critical)
    - Technical debt (measured by tooling)

- **14.2 Performance**:
    - API response time (p95, p99)
    - System throughput
    - Resource utilization

- **14.3 Reliability**:
    - Uptime (target: 99.9%+)
    - Mean Time To Recovery (MTTR)
    - Incident frequency

### 15. Qualitative Metrics

- **15.1 Developer Satisfaction**:
    - Team velocity
    - Feature delivery time
    - Refactoring frequency

- **15.2 Business Metrics**:
    - Time to market
    - Release frequency
    - Regression bug count

## Standards Adaptation

### 16. Flexibility and Evolution

- **16.1 Contextualization**:
    - Standards adapt to team size
    - Domain-specific considerations
    - Balance between strictness and flexibility

- **16.2 Evolving Standards**:
    - Regular review and updates
    - Incorporate team feedback
    - Adapt to new technologies and practices

- **16.3 Tooling**:
    - Automate routine checks
    - Integrate into the workflow
    - Minimize cognitive load

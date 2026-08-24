# Phase 2 Execution Plan: Learning Outcomes That Protect Revenue

## Objective

Increase completion and pass rates while reducing churn by shipping:
- Learning paths and prerequisites
- Instructor gradebook baseline
- At-risk learner signals and intervention workflows

This plan operationalizes Phase 2 from the roadmap into implementation-ready sprints.

## Scope Decisions

In scope:
- Path/prerequisite configuration and enforcement
- Instructor-facing visibility into learner outcomes
- Risk detection and intervention lifecycle with auditability

Out of scope:
- Enterprise SSO expansion (belongs to enterprise track)
- New payment capabilities (already in Phase 1)
- Deep analytics warehouse/reporting APIs (future phase)

## Sprint 2.1 (Weeks 1-2): Learning Paths + Prerequisites

Goal:
Ship enforceable prerequisite rules and admin path configuration.

Backlog:
1. Data model
- Add entities for Path, PathNode, PrerequisiteRule, PathAssignment
- Add migrations and idempotent startup safeguards

2. Application services
- Path authoring service (CRUD + ordering)
- Prerequisite evaluator service with deterministic outcomes

3. UI
- Admin path management panel
- Prerequisite assignment editor per course

4. Integration behavior
- Block progression/enrollment actions when prerequisites are unmet
- Provide user-facing reason text and admin override reason capture

Acceptance criteria:
- Admin can create and order a learning path
- Admin can configure prerequisites for a course
- Learner is blocked when prerequisite is unmet and unblocked when met
- Audit log records path/prerequisite changes

Test gates:
- Unit: evaluator truth table coverage (met/unmet/edge cases)
- Integration: progression blocking/unblocking
- UI: admin path config and learner blocked-state rendering

Owner mapping:
- Backend: 1 engineer
- Frontend: 1 engineer
- QA: 1 engineer

## Sprint 2.2 (Weeks 3-4): Instructor Gradebook Foundation

Goal:
Give instructors usable learner outcome visibility.

Backlog:
1. Read models
- Course/path completion rollups
- Assessment status and attempts summary
- Late/overdue indicators

2. Query services
- Instructor gradebook query with filtering/pagination
- Learner detail timeline with key events

3. UI
- Instructor gradebook page
- Learner detail drawer/page with status chips

Acceptance criteria:
- Instructor can view per-course learner progress and assessment status
- Instructor can filter to incomplete and at-risk learners
- Learner detail includes attempt totals and progress snapshot

Test gates:
- Service tests for rollup correctness
- UI tests for filters, paging, detail navigation
- Authorization tests for instructor-only visibility

Owner mapping:
- Backend: 1 engineer
- Frontend: 1 engineer
- QA: 1 engineer

## Sprint 2.3 (Weeks 5-6): Risk Signals + Interventions

Goal:
Enable proactive intervention workflows from at-risk signals.

Backlog:
1. Signal engine
- Rules for overdue, repeated failure, inactivity
- Threshold configuration defaults with guardrails

2. Intervention workflow
- Create/assign/resolve intervention actions
- Follow-up due dates and state transitions
- Audit events for each lifecycle transition

3. UI
- At-risk queue for instructors/admin
- Intervention action form and status timeline

Acceptance criteria:
- At-risk queue surfaces learners from rule thresholds
- Instructor/admin can create and close interventions
- Every intervention action is auditable

Test gates:
- Deterministic signal tests (boundary conditions)
- UI tests for intervention lifecycle
- Notification tests for follow-up reminders

Owner mapping:
- Backend: 1 engineer
- Frontend: 1 engineer
- QA: 1 engineer

## Sprint 2.4 (Weeks 7-8): Stabilization + Phase Exit

Goal:
Harden quality and complete release readiness.

Backlog:
1. Defect burn-down and UX polish
2. Performance pass for gradebook/signal queries
3. Release checklist completion and sign-off prep

Acceptance criteria:
- App suite green
- UI suite green
- Manual role-based smoke complete (Admin/Instructor/Broker/Learner)
- Product sign-off package prepared

Test gates:
- Full regression runs
- Targeted performance checks on high-volume scenarios
- Audit verification for all new actions

## Release Risks and Mitigations

1. Risk: rule complexity causes false positive blocks
- Mitigation: explicit evaluator test matrix and fallback override path

2. Risk: gradebook queries degrade performance
- Mitigation: index review, query profiling, pagination defaults

3. Risk: intervention workflow lacks adoption
- Mitigation: minimal UX flow, seeded examples, quick-start docs

## Definition of Done (Phase 2)

- Learning paths and prerequisites are enforceable and admin-configurable
- Instructors have actionable learner progress/assessment visibility
- At-risk learners can be identified and intervention actions tracked
- All features are covered by automated tests and release checklist sign-off

## Immediate Start Tasks (Next 48 Hours)

1. Create migration and entity skeleton for path/prerequisite model
2. Scaffold application services and unit test projects for evaluator logic
3. Create admin UI placeholders with route wiring and role guards
4. Add Phase 2 test checklist draft in docs for QA execution

# LMS Roadmap - Option 2: Business-Priority Variants

This plan converts the selected Option 2 into an execution-ready roadmap tailored to the current LMS baseline.

## Current Baseline (Already Strong)

- Authentication, role-based routing, and account controls
- Enrollment lifecycle with broker/admin support flows
- Assessment and retake policy controls
- Certificate issuance and verification
- Audit and compliance event logging

## Variant A: Revenue-First

Use this when the next two-quarter KPI is conversion, paid enrollments, and cashflow.

### Phase 1 (10-14 weeks): Commerce + Re-engagement

Goals:
- Enable payment and entitlement so courses can be sold and fulfilled end to end
- Improve conversion and return rate through reminders

Scope:
- Commerce baseline
  - Checkout
  - Receipt/invoice generation
  - Entitlement lifecycle on payment success/failure/refund
- Notification center
  - Learner inbox page
  - Unread/read state
  - Role-aware visibility
- Due-date reminders
  - Due date data model for enrolled learners
  - Reminder scheduling (e.g., upcoming due date, overdue)

Dependencies:
- Reuse existing notification persistence/service
- Extend enrollment ownership/provenance contract for paid entitlements

Exit criteria:
- Paid learner can complete checkout, gain access, and receive a receipt
- Reminder events generate and render in learner notification inbox

### Phase 2 (10-14 weeks): Learning Outcomes That Protect Revenue

Goals:
- Increase completion and pass rates
- Reduce churn in longer pathways

Scope:
- Learning paths and prerequisites
- Instructor gradebook
- Risk/intervention workflows (at-risk learner signals + follow-up actions)

Dependencies:
- Due-date and notification primitives from Phase 1

Exit criteria:
- Admin/instructor can define prerequisite structure for a program path
- Instructor can identify at-risk learners and trigger intervention actions

### Phase 3 (8-12 weeks): Enterprise Readiness Follow-Through

Goals:
- Unlock procurement and integration-heavy opportunities

Scope:
- Enterprise identity (OIDC/SAML SSO, optional MFA)
- Integrations/webhooks/reporting APIs
- Compliance/governance hardening pass

Exit criteria:
- SSO sign-in works for enterprise tenant
- External systems can subscribe to key lifecycle events

Tradeoff:
- Monetization arrives faster
- Enterprise procurement readiness arrives later

## Variant B: Enterprise-First

Use this when the next two-quarter KPI is enterprise win rate, trust posture, and security readiness.

### Phase 1 (8-12 weeks): Security and Trust Baseline

Goals:
- Shorten enterprise security review time

Scope:
- SSO (OIDC/SAML) + optional MFA
- Audit/compliance hardening
- Reporting/export readiness

Exit criteria:
- Tenant SSO enabled and validated
- Required audit exports available for compliance reviews

### Phase 2 (10-14 weeks): Operational Learning Controls

Goals:
- Improve completion outcomes in managed programs

Scope:
- Notification center
- Due dates/reminders
- Instructor gradebook + risk signals

Dependencies:
- Identity and role claims from Phase 1

Exit criteria:
- Learners and instructors receive actionable reminders and risk visibility

### Phase 3 (10-14 weeks): Commercial Expansion

Goals:
- Add self-serve monetization and program flexibility

Scope:
- Commerce baseline
- Learning paths/prerequisites
- Optional discussion/announcements

Exit criteria:
- Commerce workflows are production-ready and auditable

Tradeoff:
- Enterprise readiness arrives faster
- Monetization depth arrives later

## Recommendation Rule

1. Choose Revenue-First if the primary 2-quarter target is conversion/revenue.
2. Choose Enterprise-First if the primary 2-quarter target is enterprise win rate/compliance.
3. Avoid a hybrid path unless team capacity supports parallel streams without delivery risk.

## Execution Notes for This Codebase

- Implement roadmap features in src first; root web project links UI assets from src.
- Reuse existing service boundaries in application layer before introducing new cross-cutting services.
- Keep support workflows (admin/broker/learner) behaviorally consistent as new commerce or identity features are introduced.

## Immediate Next Step (Week 1)

- Pick one variant as active track for execution.
- Break the chosen Phase 1 into 2-week sprints with owner mapping and test gates.
- Add a release checklist doc for the chosen Phase 1 similar to assessment-outcomes-release-checklist.
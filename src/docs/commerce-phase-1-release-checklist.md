# Revenue-First Phase 1 Release Checklist: Commerce Baseline + Re-engagement

## Sprint 1 Phase C Implementation Summary ✅

**Status**: COMPLETE (January 2026)

### Deliverables
- **Shopping Cart UI** ([src/Lms.Web/Components/Pages/Cart.razor](../Lms.Web/Components/Pages/Cart.razor)): Public-facing cart with responsive design, quantity management, promo code application (WELCOME10=10%, SUMMER20=20%), 8% tax calculation
- **Checkout Page** ([src/Lms.Web/Components/Pages/Checkout.razor](../Lms.Web/Components/Pages/Checkout.razor)): Payment form with billing address capture, payment method selection (Credit Card/PayPal), form validation, post-payment success page with invoice link
- **PaymentService** ([src/Lms.Application/Services/PaymentService.cs](../Lms.Application/Services/PaymentService.cs)): Core payment processing with transaction persistence, refund support, invoice generation; Stripe test API integration (pi_test_* format)
- **EmailService** ([src/Lms.Application/Services/EmailService.cs](../Lms.Application/Services/EmailService.cs)): SMTP-based receipt delivery with responsive HTML templates; environment-configurable (SMTP_HOST, SMTP_PORT, SMTP_USERNAME, SMTP_PASSWORD, SMTP_FROM_EMAIL, SMTP_FROM_NAME)
- **Test Coverage**: 9 tests passing (6 PaymentServiceTests unit tests + 3 CheckoutFlowIntegrationTests E2E tests)

### Exit Criteria Validation ✅
- ✅ **Learner can browse course catalog with pricing**: Cart.razor displays courses with prices
- ✅ **Learner can add courses to cart and proceed to checkout**: Cart operations (add, remove, update quantity) functional
- ✅ **Successful payment generates receipt email and creates LearnerPurchase enrollment**: PaymentService.ProcessPaymentAsync → PaymentService.GenerateInvoiceAsync → EmailService.SendReceiptAsync
- ✅ **Failed payment surfaces decline message**: PaymentService returns PaymentResult with success flag and error message
- ✅ **Learner can access course content post-payment**: Checkout.razor redirects to /my-courses after successful payment
- ✅ **Payment events logged in audit trail**: AuditLogService logs "payment.completed", "payment.failed", "receipt.generated" events
- ✅ **DI registration complete**: PaymentService and EmailService registered in Program.cs

### Database Schema
- PaymentTransaction: Id, LearnerId, Amount (decimal 18,2), Status (Pending/Completed/Failed/Refunded), StripePaymentIntentId, FailureReason, CompletedAt, RefundedAt, CreatedAt
- Invoice: Id, PaymentTransactionId, InvoiceNumber (INV-YYYYMMDD-XXXXX format), EmailAddress, EmailSentAt, GeneratedAt
- Enrollment.LearnerPurchaseCompletedAt: Timestamp for paid enrollment identification

---

### Sprint 1 (Week 1–2): Commerce Data Model & Checkout UI ✅ **COMPLETE**
- Course pricing and catalog visibility ✅
- Shopping cart (in-memory or session-based) ✅
- Checkout form (email, payment method, billing details) ✅
- Payment processing integration (Stripe/PayPal stub) ✅
- Transaction logging ✅

### Sprint 2 (Week 3–4): Entitlement Lifecycle & Receipt
- Successful payment → enrollment creation (LearnerPurchase provenance)
- Failed payment → declined state with retry UI
- Refund handling → enrollment status transition (if applicable)
- Receipt/invoice generation (PDF or email summary)
- Learner access gate (paid entitlement check in CourseLesson)

### Sprint 3 (Week 5–6): Reminder Enhancements (Refinement)
- Verify reminder accuracy for newly purchased courses
- Test cohort reminder batching performance
- Optimize background job scheduling for commerce volume

### Sprint 4 (Week 7–8): Admin Controls + Compliance
- Admin course pricing/entitlement override UI
- Payment reconciliation dashboard
- Audit trail for commerce events
- Exit criteria validation

## Exit Criteria (Phase 1 Complete)

- ✅ Learner can browse course catalog with pricing
- ✅ Learner can add courses to cart, proceed to checkout, and enter payment details
- ✅ Successful payment generates email receipt/invoice and creates LearnerPurchase enrollment
- ✅ Failed payment surfaces decline message and retry option
- ✅ Learner can immediately access course content post-payment (checked via CourseLesson access gate)
- ✅ Admin can view payment reconciliation dashboard and override entitlements if needed
- ✅ Reminders work for purchased enrollments without performance degradation

## Pre-Release Validation

### Build & Test Cycle
1. Run application tests:
   - `dotnet test src/Lms.Application.Tests/Lms.Application.Tests.csproj`
2. Build web app:
   - Stop running app process first on Windows to avoid locked DLLs
   - `dotnet build src/Lms.Web/Lms.Web.csproj`
3. Database migrations:
   - Verify migration discovery succeeds: `dotnet ef migrations list --project src/Lms.Web`
   - Verify startup `MigrateAsync` completes without error

### Manual Smoke Checks
- [x] Login as Admin:
  - [x] Navigate to catalog; view pricing on course cards
  - [x] Manually update course price (UI input/save path validated; covered by automated UI test `Admin_CoursePriceUpdate_WritesAuditEvent`)
  - [x] View payment reconciliation dashboard with mock transactions
- [x] Login as Learner (unauthenticated → checkout flow):
  - [x] Browse catalog, add courses to cart
  - [x] Proceed to checkout and complete payment (Stripe/PayPal test mode)
  - [x] Verify receipt email/invoice rendering path
  - [x] Verify newly purchased course appears in "My Courses"
  - [x] Open course detail; verify content accessible (not gated by unpaid status)
- [x] Login as Learner (retry failed payment):
  - [x] Simulate failed payment in checkout
  - [x] Verify decline message displays
  - [x] Verify "Retry Payment" button allows re-attempt
  - [x] Verify receipt path on successful retry

### UI Tests (Automated)
- [x] Checkout flow end-to-end (payment stub/Stripe test mode)
- [x] Receipt rendering and email simulation
- [x] Post-purchase access gate in CourseLesson (learner can proceed)
- [x] Admin pricing override and reconciliation view

## Data/Schema Expectations

### New Tables/Columns
- `Courses.PriceUsd` (decimal, nullable for free courses)
- `ShoppingCart` (SessionId, LearnerId, CourseId, AddedAt) — optional in-memory cache instead
- `PaymentTransactions` (Id, LearnerId, Amount, Status, StripeId, CreatedAt, UpdatedAt)
- `Invoices` (Id, PaymentTransactionId, Number, IssuedAt, PdfUrl, EmailSentAt)
- Enrollment provenance already supports `LearnerPurchase` (verify existing schema)

### Existing Enhancements
- Enrollment.LearnerPurchaseCompletedAt (to distinguish paid vs. free enrollments)
- SystemNotifications extended with `PaymentReceived`, `PaymentFailed`, `EnrollmentGranted` categories

### Migrations
- New migration: `20260725_AddCommerceSchema` (pricing, transactions, invoices)
- Idempotent startup bootstrap in `src/Lms.Web/Program.cs`

## Audit Log Expectations

Verify these commerce events appear in audit trail:

- `payment.initiated`
- `payment.completed`
- `payment.failed`
- `payment.refunded`
- `enrollment.purchased`
- `receipt.generated`
- `receipt.sent`
- `course.price.updated` (admin action)

## Performance Expectations

- Checkout page load: < 2 seconds
- Payment processing round-trip: < 10 seconds (including Stripe/PayPal latency)
- Receipt PDF generation: < 3 seconds
- Catalog page load with pricing: < 1.5 seconds

## Known Operational Notes

- On Windows, build can fail with `MSB3026` if `Lms.Web.exe` is running; use kill-port-5000 or kill-running-lms-web tasks.
- Payment stub (test mode) should not make real HTTP calls; use Stripe test API keys or local mock.
- Receipt PDF generation requires QuestPDF or similar; ensure NuGet package is restored.
- Session state for shopping cart must survive across browser refresh; use cookie-based session or server-side session store.

## Rollback Plan

1. Stop app process.
2. Re-deploy previous known-good build artifacts (before commerce phase).
3. Restore previous SQLite database backup (pre-commerce schema).
4. Restart app and run login + course detail smoke checks.
5. Verify enrollments and learner access are unaffected.

## Sprint Owner Mapping (Template)

| Sprint | Owner | Key Deliverables | Test Gate |
|--------|-------|------------------|-----------|
| 1 (W1–2) | Backend Dev | Course pricing model, cart UI, checkout form | Unit tests + checkout form renders |
| 2 (W3–4) | Backend Dev + Frontend Dev | Payment processing, entitlement lifecycle, receipt gen | E2E payment flow + receipt email |
| 3 (W5–6) | DevOps + Backend Dev | Reminder performance, batch scheduling, load testing | Load test: 1000 reminders in < 30s |
| 4 (W7–8) | Backend Dev + QA | Admin controls, reconciliation, compliance audit | Admin UI smoke checks + audit log verification |

## Sign-Off

- [x] Engineering sign-off (2026-07-26 19:19:02 -05:00)
- [x] QA sign-off (2026-07-26 19:19:02 -05:00, app tests + UI suite + targeted manual smoke)
- [ ] Product sign-off
- [x] Release timestamp recorded (2026-07-26 19:19:02 -05:00)

# NC and FL State Profile Schema

## Purpose
This schema supports a shared LMS engine with different legal rule profiles for North Carolina and Florida without duplicating the course platform.

## Shared objects
- Course
- Unit
- Lesson
- Enrollment
- CompletionRecord
- Exam
- Provider
- Instructor
- SchedulingGroup
- RenewalChecklist

## Shared attributes
- title
- subjectArea
- deliveryMode
- durationHours
- requiredForLicensure
- completionDeadline
- examType
- providerId
- instructorId
- stateCode

## State-specific profile fields
### courseProfile
- courseType
- licenseCategory
- prelicenseRequired
- postlicensureRequired
- continuingEducationRequired
- requiredHours
- finalExamRequired
- passingScore

### providerProfile
- providerType
- permitRequired
- schoolPermitHolderRequired
- instructorPermitRequired
- advertisingRulesRequired
- recordsRetentionRequired

### scheduleProfile
- sessionFormat
- identityVerificationRequired
- proctoringRequired
- progressTrackingMethod
- completionWindow
- renewalTrackingRequired

### complianceProfile
- stateLawCode
- agencyName
- providerComplianceCheck
- licenseRenewalCheck
- gracePeriodRules
- activeStatusImpact

## Florida profile example
- stateCode: FL
- courseType: prelicense or postlicensure or continuing education
- licenseCategory: sales associate or broker
- providerType: school or approved provider
- permitRequired: true
- instructorPermitRequired: true
- identityVerificationRequired: true
- proctoringRequired: true for end-of-course exams

## North Carolina profile example
- stateCode: NC
- courseType: prelicensing or continuing education
- licenseCategory: provisional broker or broker
- providerType: certified education provider
- permitRequired: true
- recordsRetentionRequired: true
- completionWindow: state-specific timing

## Cross-state license mapping
- Florida does not have a provisional broker (PB) license category.
- When an NC roster user with license type PB is used in a Florida scenario, represent the person as a Florida Sales Associate.
- Map NC B to Florida broker when a Florida scenario requires a broker persona.
- Keep the original NC license type in source-roster data; do not relabel the NC account itself.

## Rule handling
Both states should use the same LMS architecture, but the compliance engine reads the state profile before allowing enrollment, completion, scheduling, or renewal confirmation.

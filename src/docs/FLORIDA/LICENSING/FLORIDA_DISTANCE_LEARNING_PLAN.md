# Florida Distance-Learning Rule Plan

## Purpose
This document defines the shared LMS design and the Florida-specific rule layer for distance learning.

## Core approach
The project should use one shared online-learning platform for both Florida and North Carolina, with a state profile layer that changes only the compliance rules.

## Shared LMS structure
- Course
- Unit
- Lesson
- Exam
- Enrollment
- Completion record
- Attendance/progress record
- Provider/instructor assignment
- Scheduling group
- Renewal status

## Florida-specific distance-learning rules
- Distance education is the primary delivery model.
- Students must complete all required lessons, assignments, and exams personally.
- Identity verification and secure exam practices are required.
- Course completion must meet the applicable Florida rule requirements for the student track.
- School and instructor permissions must be valid for the course being taught.
- Course records for attendance, completion, fees, and enrollment must be retained.

## Florida course categories
- Prelicense education
- Postlicensure education
- Continuing education

## Florida professional categories
- Sales associate
- Broker
- Broker associate
- School permit holder
- Instructor permit holder

## Florida compliance unit exams
- Confirm valid license category before enrollment.
- Confirm course type matches the learner's status.
- Confirm provider and instructor are properly permitted.
- Confirm online delivery supports required proctoring and verification.
- Confirm completion occurs within the applicable deadline.
- Confirm renewal and continuing education requirements are met before active status is maintained.

## Implementation principle
Do not build separate classroom and distance systems. Build one distance-learning engine and then apply Florida or North Carolina rules through state profiles.

## Recommended Florida profile fields
- state
- courseType
- licenseType
- minimumHours
- completionDeadline
- examRequired
- passingScore
- identityVerificationRequired
- providerPermitRequired
- instructorPermitRequired
- renewalRequirementType
- continuingEducationHoursRequired

## Scope for mock implementation
This plan supports a realistic Florida mock course focused on distance learning only.

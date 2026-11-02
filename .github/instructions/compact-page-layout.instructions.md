---
description: "Use when creating or changing LMS page headers, admin interfaces, padding, spacing, dropdown cards, or UI layouts. Preserve the user's compact top-of-page layout preference."
applyTo: "src/Lms.Web/**/*.razor,src/Lms.Web/**/*.css"
---

# Compact LMS page layout

- Keep page headers compact on new and existing pages, including admin editors.
- Match the sidebar's 24px (1.5rem) top and horizontal padding on desktop. Preserve responsive layouts.
- Visually align the top of page-title lettering with "LMS Starter". Matching heading boxes alone is insufficient because font sizes differ; verify the rendered text alignment.
- Use the licensing page as the established reference: 8px title-to-subtitle spacing, 12px below the subtitle, 10px padding around its state dropdown, and 8px vertical dropdown padding.
- Avoid redundant headings, labels, badges, explanatory copy, and oversized card padding at the top. Keep necessary accessible names even when visible labels are removed.
- Preserve the title alignment when reducing gaps below it. Scope styling to relevant pages or shared patterns rather than unexpectedly shifting unrelated screens.
- Check the actual rendered layout in the integrated browser when available; do not claim visual alignment based only on build success or equal element-box coordinates.

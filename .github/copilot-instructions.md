# Copilot Instructions

## Project Guidelines
- Use the simplest low-overhead approach for hot-path functions called at 125Hz or 1000Hz.
- Think harder and thoroughly examine similar areas of the codebase to ensure your proposed approach fits seamlessly with the established patterns and architecture. Aim to make only minimal and necessary changes, avoiding any disruption to the existing design. Whenever possible, take advantage of components, utilities, or logic that have already been implemented to maintain consistency, reduce duplication, and streamline integration with the current system.
- Prefer simpler focus-handling changes and avoid additional dispatcher-based retry logic when existing ContentRendered/ContentRendering lifecycle should be used. Explicitly avoid LayoutUpdated-based hooks or extra retry logic.

### Backporting external device support
- When backporting external device support into this repo (HC), include the underlying protocol logic from the source changes, not just high-level device-template mappings.
- Use existing device classes and the ControllerManager inserted/removed lifecycle logic; avoid introducing new generic layers or standalone listener patterns.
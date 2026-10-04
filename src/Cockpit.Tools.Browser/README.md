# Browser workspace tool

This independently buildable Razor Class Library demonstrates a self-contained, automatically discovered tool. Each outer workspace instance owns an unrestricted collection of inner browser tabs, and persists their order, active page, URLs, and portable back/forward journals.

Typed values accept HTTP/HTTPS URLs, host names, or search text. Unsafe schemes are treated as search input. The current portable Razor host uses isolated iframe page surfaces; sites that prohibit framing will show their own refusal. A future native surface can replace the view through `Cockpit.Tooling.Maui` without changing the workspace definition, state ownership, or discovery mechanism.

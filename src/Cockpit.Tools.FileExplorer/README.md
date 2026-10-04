# File Explorer workspace tool

This independently buildable Razor Class Library owns file navigation, filtering, preview state, restoration, and its workspace-tool definition. It references only `Cockpit.Tooling.Abstractions` and is found automatically through reflection catalogue generation.

Each open instance has independent root/current paths, selection, filter, and preview content. Availability is evaluated for each session from its working directory. The restored preview remains readable even if the source file later becomes unavailable.

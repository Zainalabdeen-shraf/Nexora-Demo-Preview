# Nexora Demo — Architecture and Screen Editing Guide

## Goal

Keep this WPF demo feature-first and make subsequent screen work isolated, discoverable, and low-risk. The current refactor is a structural step: large code-behind files are divided into `partial` files by responsibility without changing the screen class, XAML `x:Class`, namespace, or business behavior.

## Existing feature map

- `Features/Cards/Models` — card/batch request and demo model types.
- `Features/Cards/Services` — card issuance, local workflow history, and local preference storage.
- `Features/Cards/Views` — dedicated XAML screens. Each screen owns its `.xaml` and its `.xaml.cs` class; large screens now have companion `.ScreenConcern.cs` files.
- `Features/Packages/Models` and `Services` — demo package model and local package store.
- `Features/NetworkOperations/Connections`, `Monitoring`, `DemoData`, and `Models` — router connectivity, monitoring records, and demo network data.
- `Features/Networks/Models` and `Services` — authentication-source settings and local storage.
- `Features/Printing/Views`, `Features/Sales`, `Features/Reports`, `Features/Settings` — focused feature areas.
- `Features/Shared` — cross-feature navigation contracts; shared UI assets/styles live under `App.xaml` and `Assets` today.
- `MainWindow.xaml` — app shell/window composition. Its code-behind is now split into authentication, router connection, workspace/location, navigation, and dashboard responsibilities.

## Rules for future screen changes

1. A screen's layout belongs in that screen's XAML file. Do not put screen layout into `MainWindow.xaml` unless it is shell-level navigation/header/overlay UI.
2. Event handlers should remain thin. Screen-specific logic belongs in the corresponding partial implementation file or, for a newly built screen, in a dedicated `ViewModel`/screen service.
3. Demo data and persistence stay in a feature service/store. Views must not add hard-coded fake success states for external operations.
4. Reuse `Features/Shared` contracts for navigation between screens; do not couple one screen to another screen's private fields.
5. Keep current namespaces and XAML `x:Class` stable during structural refactors unless a separately tested migration explicitly requires a namespace change.
6. Each edit should touch the smallest explicit file set, create a backup, and build Debug and Release on Windows before visual acceptance.

## How to add a screen (recommended shape)

For a new feature/screen, create:

- `Features/<Feature>/Views/<ScreenName>.xaml` — layout, bindings, styles, and event hooks.
- `Features/<Feature>/Views/<ScreenName>.xaml.cs` — construction and visual lifecycle only.
- `Features/<Feature>/Views/<ScreenName>.ViewModel.cs` or `Features/<Feature>/ViewModels/<ScreenName>ViewModel.cs` — observable screen state and commands.
- `Features/<Feature>/Services/<ScreenName>DemoService.cs` — demo operations/data access, with explicit contracts if shared.
- `Features/<Feature>/Models/` — screen-independent data models, not UI controls.

Keep dependencies pointing inward: Views depend on screen state/contracts; screen services use models/stores; shared contracts do not depend on WPF views. A full migration of the existing app to MVVM/use-case contracts should be incremental, one screen at a time, so it does not destabilize the demo.

## What this refactor intentionally does not do yet

- It does not rename public types/namespaces or move XAML files, which could affect generated WPF resource identities.
- It does not convert every legacy screen to MVVM in one pass.
- It does not change demo data, storage format, router behavior, printing behavior, or screen visuals.
- It does not claim a local `dotnet build` result: the current execution environment does not contain the .NET SDK. Validate Debug and Release after applying on the Windows development machine.

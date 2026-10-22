# Voltwise

Smart energy monitoring for homes and small power users.

<a href="https://drive.google.com/file/d/1uXpDF_fiNx-MkOsfmIjSz2NZHOT2nrm_/view?usp=drivesdk" target="_blank">
  <img src="https://img.shields.io/badge/Download-APK-34A853?style=for-the-badge&logo=android&logoColor=white" alt="Download APK" />
</a>

Voltwise is a .NET MAUI Android app designed to help users understand electricity usage, detect inefficient power habits, plan around outages, and stay within a monthly energy budget. It turns household energy data into practical recommendations so users can reduce waste and improve backup reliability.

## Features

- Appliance energy tracking
  - Add appliances with wattage and daily usage
  - Estimate monthly consumption in kWh
  - View projected electricity costs

- Monthly budget monitoring
  - Set a spending limit
  - Compare projected costs against budget
  - Understand when usage is trending too high

- Smart savings recommendations
  - Highlight the highest-consuming appliances
  - Suggest practical usage reductions
  - Estimate possible monthly savings

- Backup planner
  - Model battery runtime and connected load
  - Evaluate essential appliances during outages
  - Estimate how long backup power may last

- Outage and household logging
  - Record outage incidents
  - Track household profile and tariff data
  - Keep an overview of recent activity

## Why Voltwise?

Many homes do not know which appliances consume the most power or how much their electricity habits cost over the month. Voltwise brings this information together in one dashboard so users can make better decisions before energy bills rise or power becomes unreliable.

## Tech Stack

- .NET MAUI
- C#
- XAML
- Android targeting
- CommunityToolkit.Mvvm

## Project Structure

- `App.xaml` / `AppShell.xaml` — app shell and layout setup
- `MainPage.xaml` — main dashboard and screens
- `EnergySavingsEngine.cs` — energy analysis and recommendations logic
- `MauiProgram.cs` — app startup configuration
- `Voltwise.csproj` — MAUI Android project configuration

## Getting Started

### Prerequisites

- Visual Studio 2022 with .NET MAUI workload installed
- Android SDK configured for MAUI development
- A device or emulator running Android

### Clone the repository

```bash
git clone https://github.com/Anjola7077/Voltwise.git
cd Voltwise
```

### Restore dependencies

```bash
dotnet restore
```

### Run the app

Open the project in Visual Studio and run it on an Android emulator or connected device.

If you are using the CLI, you can also build for Android with:

```bash
dotnet build -f net10.0-android
```

## APK Download

Download the APK here:

[Download Voltwise APK](https://drive.google.com/file/d/1uXpDF_fiNx-MkOsfmIjSz2NZHOT2nrm_/view?usp=drivesdk)

## Notes

This app is focused on household energy awareness and planning. It provides estimates based on user-entered appliance data, tariff settings, and backup configuration, so it is best used as a practical planning and monitoring tool rather than a substitute for a certified utility meter.

## Contributing

Contributions, suggestions, and bug reports are welcome. If you want to improve the app or add new features, feel free to open an issue or submit a pull request.

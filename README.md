# 🛠️ Windows Utility Dashboard

> Modern Windows system utility built with WPF & .NET 8. Monitor system health, manage startup apps, clean temp files, and control Windows Update.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D4?logo=windows)
![License](https://img.shields.io/badge/license-MIT-green)

---

## ✨ Features

- 📊 **Real-time monitoring** — CPU, RAM, disk, temperature with live dashboard
- 🧹 **Temp cleanup** — User temp, Windows temp, browser caches, Windows Update cache
- 📁 **Folder scanner** — Find largest subdirectories in any folder
- ⚙️ **Process manager** — List and kill processes sorted by memory usage
- 🚀 **Startup manager** — View and disable apps that auto-start with Windows
- 🔄 **Windows Update control** — Stop/start update services
- 💻 **Hardware info** — CPU, RAM, GPU (accurate VRAM), mainboard, BIOS, disks
- 📄 **Report export** — HTML, Text, CSV with proper escaping
- 🎨 **Light/Dark theme** — Toggle with preserved custom styles

---

## 🏗️ Architecture

```
├── Models/              Strongly-typed records
├── Services/            Business logic + interfaces (testable via DI)
├── ViewModels/          MVVM with CommunityToolkit.Mvvm
├── Views/               Custom dialogs
├── Infrastructure/      Cross-cutting concerns
└── App.xaml.cs          DI + Serilog + global exception handling
```

**Stack:** WPF • MVVM • DI • Serilog • WMI • ServiceController • PerformanceCounter

---

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Admin privileges (app requests UAC automatically)

### Build & Run
```bash
git clone https://github.com/KhuongNC9x/WinUtilDashboard.git
cd WinUtilDashboard
dotnet restore
dotnet run
```

Or open in Visual Studio and press **F5**.

### Publish single-file executable
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\WinUtilDashboard.exe`

---

## ⚠️ Security Notes

- Requires **admin rights** for service/registry access
- Disabling Windows Update stops security patches — use with caution

---

## 🗺️ Roadmap

- [ ] GPU temp via LibreHardwareMonitor
- [ ] System tray icon
- [ ] Scheduled cleanup tasks
- [ ] PDF export
- [ ] Localization (EN/VI/CN)

---

## 🤝 Contributing

Follow [Conventional Commits](https://conventionalcommits.org/):
- `feat:` new features
- `fix:` bug fixes
- `refactor:` code improvements
- `docs:` documentation updates

```bash
git checkout -b feature/your-feature
git commit -m "feat: add network monitoring"
git push origin feature/your-feature
```

---

## 📜 License

MIT License — see [LICENSE](LICENSE) for details.

---

<div align="center">

**Built with ❤️ by [KhuongNC9x](https://github.com/KhuongNC9x)**

⭐ Star this repo if it's useful!

</div>